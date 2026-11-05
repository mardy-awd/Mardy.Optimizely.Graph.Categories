using EPiServer.DataAbstraction.RuntimeModel;
using EPiServer.Framework.TypeScanner;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Optimizely.Graph.Cms.Conventions;

namespace Mardy.Optimizely.Graph.Categories;

/// <summary>Registers legacy category fields with Optimizely Graph.</summary>
public static class PageCategoryGraphExtensions
{
    /// <summary>
    /// Adds <c>LegacyCategoryIds</c> and <c>LegacyCategoryNames</c> to every eligible concrete page
    /// type. Can be called before or after <c>AddContentGraph</c>, which must also be called.
    /// A Graph full synchronization is needed to populate existing content.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="includeType">
    /// Optional filter for page types. Rejecting a type also leaves out its concrete base types,
    /// because the Graph SDK applies a base type's fields to all types derived from it.
    /// </param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddPageCategoriesToGraph(
        this IServiceCollection services,
        Func<Type, bool>? includeType = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddLogging();

        // PostConfigure runs after every Configure, so consumer exclusions and manual
        // registrations are visible whichever order the application calls these in.
        services.AddOptions<GraphConventionOptions>()
            .PostConfigure<ITypeScannerLookup, IEnumerable<IContentTypeModelFilter>, IEnumerable<IContentTypeBaseProvider>, ILoggerFactory>(
                (options, typeScanner, modelFilters, baseProviders, loggerFactory) =>
                    PageCategoryConventionRegistrar.Apply(
                        options,
                        typeScanner.AllTypes,
                        modelFilters,
                        baseProviders,
                        includeType,
                        loggerFactory.CreateLogger(typeof(PageCategoryGraphExtensions).FullName!)));

        return services;
    }
}
