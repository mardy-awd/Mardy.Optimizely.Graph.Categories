using System.Linq.Expressions;
using System.Reflection;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.DataAbstraction.RuntimeModel;
using Microsoft.Extensions.Logging;
using Optimizely.Graph.Cms.Conventions;

namespace Mardy.Optimizely.Graph.Categories;

/// <summary>
/// Adds the category fields to <see cref="GraphConventionOptions"/>. The Graph SDK validates
/// registrations when the host starts and throws on a bad one, which stops the site. Every skip
/// below prevents one of those failures.
/// </summary>
internal static class PageCategoryConventionRegistrar
{
    private static readonly Field[] Fields =
    [
        new(nameof(PageCategoryFields.LegacyCategoryIds), BodyOf(page => page.LegacyCategoryIds(default!))),
        new(nameof(PageCategoryFields.LegacyCategoryNames), BodyOf(page => page.LegacyCategoryNames(default!))),
    ];

    public static void Apply(
        GraphConventionOptions options,
        IEnumerable<Type> scannedTypes,
        IEnumerable<IContentTypeModelFilter> modelFilters,
        IEnumerable<IContentTypeBaseProvider> baseProviders,
        Func<Type, bool>? includeType,
        ILogger logger)
    {
        var pageTypes = FindPageTypes(options, scannedTypes, modelFilters, baseProviders);
        var rejectedByCaller = includeType is null ? [] : pageTypes.Where(type => !includeType(type)).ToList();

        foreach (var field in Fields)
        {
            var conflicting = pageTypes.Where(type => DeclaresMember(type, field.Name)).ToList();
            foreach (var type in conflicting)
            {
                logger.LogWarning(
                    "Graph field {Field} was not added to {PageType} because it has a property with the same name.",
                    field.Name,
                    type.FullName);
            }

            var rejected = rejectedByCaller.Concat(conflicting).ToList();
            var existingOwners = options.ComputedFields
                .Where(entry => entry.Value.Any(definition => HasName(definition, field.Name)))
                .Select(entry => entry.Key)
                .ToList();

            foreach (var type in pageTypes)
            {
                // The SDK merges fields down the inheritance chain, so a concrete base of a rejected
                // type must be left out too, or the rejected type would still inherit the field.
                if (rejected.Any(type.IsAssignableFrom))
                {
                    continue;
                }

                // A caller's registration wins, including one on a base type or contract.
                if (existingOwners.Any(owner => owner.IsAssignableFrom(type)))
                {
                    continue;
                }

                if (!options.ComputedFields.TryGetValue(type, out var definitions))
                {
                    definitions = [];
                    options.ComputedFields[type] = definitions;
                }

                definitions.Add(new GraphComputedFieldDefinition(field.Body, IndexingType.Queryable));
            }
        }
    }

    private static List<Type> FindPageTypes(
        GraphConventionOptions options,
        IEnumerable<Type> scannedTypes,
        IEnumerable<IContentTypeModelFilter> modelFilters,
        IEnumerable<IContentTypeBaseProvider> baseProviders)
    {
        // Same starting set as the CMS model scanner, so filters see what they would see there.
        IList<Type> models = scannedTypes
            .Where(type => typeof(IContentData).IsAssignableFrom(type) && (!type.IsAbstract || type.IsInterface))
            .Distinct()
            .ToList();

        foreach (var filter in modelFilters)
        {
            models = filter.Filter(models);
        }

        // Base models such as ExperienceData are shared by content types and may have no content
        // type of their own, in which case the SDK's validation would fail.
        var baseModels = baseProviders
            .SelectMany(provider => provider.ContentTypeBases.Select(provider.Resolve))
            .Where(type => type is not null)
            .ToHashSet();

        return models
            .Where(type => typeof(PageData).IsAssignableFrom(type)
                && type != typeof(PageData)
                && !type.IsAbstract
                && !type.ContainsGenericParameters
                && !baseModels.Contains(type)
                && !options.ExcludedContentTypes.Any(excluded => excluded.IsAssignableFrom(type)))
            .ToList();
    }

    // Case-insensitive to match the SDK's own name comparison.
    private static bool DeclaresMember(Type type, string name)
        => type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Any(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase));

    private static bool HasName(GraphComputedFieldDefinition definition, string name)
    {
        var body = definition.Body is UnaryExpression { NodeType: ExpressionType.Convert } convert
            ? convert.Operand
            : definition.Body;

        return body is MethodCallExpression call
            && string.Equals(call.Method.Name, name, StringComparison.OrdinalIgnoreCase);
    }

    private static Expression BodyOf(Expression<Func<PageData, object>> field) => field.Body;

    private sealed record Field(string Name, Expression Body);
}
