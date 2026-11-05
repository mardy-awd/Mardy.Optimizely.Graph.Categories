using EPiServer.Core;
using EPiServer.DataAbstraction;
using Microsoft.Extensions.DependencyInjection;

namespace Mardy.Optimizely.Graph.Categories;

/// <summary>
/// Computed Graph fields for <see cref="PageData.Category"/>. The Graph SDK uses each method's
/// name as the field name, so renaming a method changes the Graph contract.
/// </summary>
/// <remarks>
/// In query expressions, pass <c>default!</c> for <see cref="IServiceProvider"/>. The SDK reads
/// the expression without invoking the method, and supplies the real provider when indexing.
/// </remarks>
public static class PageCategoryFields
{
    /// <summary>Gets the IDs of the categories assigned to the page. IDs are specific to each database.</summary>
    /// <param name="page">The page being indexed.</param>
    /// <param name="services">Service provider supplied by the Graph SDK.</param>
    /// <returns>The IDs of the assigned categories that still exist. Empty if there are none.</returns>
    public static List<int> LegacyCategoryIds(this PageData page, IServiceProvider services)
        => ResolveCategories(page, services).Select(category => category.ID).ToList();

    /// <summary>Gets the names of the categories assigned to the page. Names are unique only among siblings.</summary>
    /// <param name="page">The page being indexed.</param>
    /// <param name="services">Service provider supplied by the Graph SDK.</param>
    /// <returns>The names of the assigned categories that still exist. Empty if there are none.</returns>
    public static List<string> LegacyCategoryNames(this PageData page, IServiceProvider services)
        => ResolveCategories(page, services).Select(category => category.Name).ToList();

    private static IEnumerable<Category> ResolveCategories(PageData page, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(services);

        var assigned = page.Category;
        if (assigned is null || assigned.IsEmpty)
        {
            return [];
        }

        var repository = services.GetRequiredService<CategoryRepository>();
        return assigned
            .Select(repository.Get)
            .Where(category => category is not null);
    }
}
