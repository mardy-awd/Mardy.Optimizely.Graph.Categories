# Mardy.Optimizely.Graph.Categories

Makes legacy `PageData.Category` values queryable and filterable in Optimizely Graph on CMS 13 PaaS, with no changes to your page models.

It adds two fields to every concrete page type:

| Field | Type | Contains |
|---|---|---|
| `LegacyCategoryIds` | `[Int]` | The IDs of the categories assigned to the page |
| `LegacyCategoryNames` | `[String]` | The **Name** of each assigned category, not its Display Name |

The add-on uses only public CMS and Graph APIs. It has no custom mappers, reflection against internal types or manual schema changes, and the fields survive a full sync.

> **Status:** proof of concept. It isn't on NuGet yet, so reference the project from source for now.

## Requirements

- .NET 10
- CMS 13 with `Optimizely.Graph.Cms` 13.4.0 or later, below 14.0.0

Live testing has been on 13.5.0. At 13.4.0, only the unit tests have been run.

## Setup

1. Register the add-on alongside `AddContentGraph`, in either order:

   ```csharp
   using Mardy.Optimizely.Graph.Categories;

   services.AddContentGraph();
   services.AddPageCategoriesToGraph();
   ```

2. Start the site, then run **Optimizely Graph Full Synchronization** under **Settings > Scheduled Jobs**. This adds the fields to the schema and fills in values for existing content.

## Querying

### GraphQL

```graphql
{
  StandardPage(where: { LegacyCategoryNames: { in: ["Track"] } }) {
    total
    items {
      _metadata { displayName }
      LegacyCategoryIds
      LegacyCategoryNames
    }
  }
}
```

To filter by ID, use `LegacyCategoryIds: { in: [4] }`. A page with several categories matches if any one of them matches.

### C#

This uses the `Optimizely.Graph.Cms.Query` package, which you need to install separately. The field methods need an argument so the expression compiles, but `default!` is fine because the SDK doesn't call them.

```csharp
using Mardy.Optimizely.Graph.Categories;
using Optimizely.Graph.Cms.Query;
using Optimizely.Graph.Cms.Query.Abstractions;
using Optimizely.Graph.Cms.Query.Filtering;

var pages = await graphClient
    .QueryContent<StandardPage>()
    .Where(x => x.LegacyCategoryNames(default!).ContainsAny("Track"))
    .IncludeTotal()
    .GetAsContentAsync();
```

To read the values back, project them into a class whose property names match the fields:

```csharp
public class CategoryValues
{
    public List<int> LegacyCategoryIds { get; set; } = [];
    public List<string> LegacyCategoryNames { get; set; } = [];
}

var result = await graphClient
    .QueryContent<StandardPage>()
    .Where(x => x.LegacyCategoryIds(default!).ContainsAny(4))
    .Fields<CategoryValues>(x => x.LegacyCategoryIds, x => x.LegacyCategoryNames)
    .GetAsync();
```

## Run a full sync after changing categories

Renaming or deleting a category **doesn't update Graph, and neither does the delta sync job**. Filters keep matching the old values until you run **Optimizely Graph Full Synchronization**.

| Change | Before a full sync | After a full sync |
|---|---|---|
| Rename | The old name still matches and the new name doesn't. IDs aren't affected. | The new name matches |
| Delete | The name and ID still match | Neither matches |

Changing a category's Display Name has no effect on Graph, because Display Names aren't indexed.

## Filter on the Name, not the Display Name

A category has a **Name** and a separate **Display Name**. Editors see the Display Name in the page's category picker, but `LegacyCategoryNames` contains the Name. If a category's Name is `Track` and its Display Name is `Alloy Track`, filtering on `"Alloy Track"` returns nothing. You can see both values under **Settings > Categories**.

## Limitations

- **No filtering across page types.** The fields exist on each concrete type, not on `_Page`, so filter each type separately or use several root fields in one query. `_Page(where: { LegacyCategoryNames: ... })` is a GraphQL error, and the same filter on `QueryContent<PageData>()` throws `NotSupportedException`.
- **Don't follow the suggestion in that exception's message.** It recommends `ForInstancesOf<PageData>().IncludeField(...)`. From reading the SDK's code, that registration is expected to fail validation and stop the site at startup, because `PageData` isn't a registered content type. This hasn't been confirmed by running it.
- **Fixed field names.** The SDK takes the name from the method, so it can't be configured.
- **No hierarchy expansion.** Filtering on a parent category doesn't match pages assigned only to its children.
- **Names aren't globally unique.** They're unique only among siblings, so two categories with different parents can share a name.
- **IDs are specific to each database.** They can differ between environments, so avoid hard-coding them across environments.
- **Pages only.** Blocks and media don't get these fields.
- **Types without a CLR model get no fields.** That includes content types defined only in the CMS database.

## Which page types are included

Every concrete class derived from `PageData` that the CMS discovers gets the fields, unless:

- It's excluded through the Graph conventions.
- It's removed by an `IContentTypeModelFilter`.
- It's a base model from an `IContentTypeBaseProvider`, such as an Experience.
- You've already registered a field with the same name. Your registration is kept.

### Choosing page types yourself

```csharp
services.AddPageCategoriesToGraph(includeType: type => type != typeof(InternalPage));
```

Rejecting a type also leaves out its concrete base types, because the SDK applies a base type's fields to everything derived from it.

### Name conflicts

If a page type has a CLR property named `LegacyCategoryIds` or `LegacyCategoryNames` (ignoring case), that field is skipped for the type and its concrete base types, and a warning is logged:

```text
Graph field LegacyCategoryNames was not added to MyPage because it has a property with the same name.
```

A property with one of these names defined only in the CMS database can't be detected this way and can stop the site at startup. Use `includeType` to leave that page type out.

## Building from source

The solution needs both nuget.org and the Optimizely feed, which are already listed in `nuget.config`.

```powershell
dotnet test Mardy.Optimizely.Graph.Categories.sln
```

To run the unit tests against a newer SDK:

```powershell
dotnet test Mardy.Optimizely.Graph.Categories.sln -p:GraphCmsVersion=13.5.0
```

## License

[MIT](LICENSE)
