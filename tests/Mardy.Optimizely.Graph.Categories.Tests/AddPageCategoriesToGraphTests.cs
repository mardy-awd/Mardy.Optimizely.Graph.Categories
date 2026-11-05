using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.DataAbstraction.RuntimeModel;
using EPiServer.Framework.TypeScanner;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Optimizely.Graph.Cms.Conventions;
using Optimizely.Graph.DependencyInjection;
using Xunit;

namespace Mardy.Optimizely.Graph.Categories.Tests;

public class AddPageCategoriesToGraphTests
{
    private const string Ids = nameof(PageCategoryFields.LegacyCategoryIds);
    private const string Names = nameof(PageCategoryFields.LegacyCategoryNames);

    // Type discovery

    [Fact]
    public void Registers_both_fields_on_concrete_page_types()
    {
        var options = Build([typeof(ArticlePage), typeof(NewsPage)]);

        Assert.Equal([Ids, Names], FieldsOn(options, typeof(ArticlePage)));
        Assert.Equal([Ids, Names], FieldsOn(options, typeof(NewsPage)));
    }

    [Fact]
    public void Registers_derived_page_types_individually()
    {
        var options = Build([typeof(ArticlePage), typeof(SpecialArticlePage)]);

        Assert.Equal([Ids, Names], FieldsOn(options, typeof(SpecialArticlePage)));
    }

    [Theory]
    [InlineData(typeof(PageData))]
    [InlineData(typeof(AbstractPage))]
    [InlineData(typeof(TeaserBlock))]
    [InlineData(typeof(IPageContract))]
    [InlineData(typeof(OpenGenericPage<>))]
    [InlineData(typeof(string))]
    public void Ignores_types_that_are_not_concrete_page_models(Type type)
    {
        var options = Build([type]);

        Assert.Empty(options.ComputedFields);
    }

    [Fact]
    public void Ignores_page_types_the_cms_scanner_did_not_find()
    {
        var options = Build([typeof(ArticlePage)]);

        Assert.False(options.ComputedFields.ContainsKey(typeof(NewsPage)));
    }

    [Fact]
    public void Ignores_page_types_removed_by_a_model_filter()
    {
        var filter = new RemovingModelFilter(typeof(NewsPage));

        var options = Build(
            [typeof(ArticlePage), typeof(NewsPage), typeof(TeaserBlock)],
            services => services.AddSingleton<IContentTypeModelFilter>(filter));

        Assert.True(options.ComputedFields.ContainsKey(typeof(ArticlePage)));
        Assert.False(options.ComputedFields.ContainsKey(typeof(NewsPage)));
        Assert.Contains(typeof(TeaserBlock), filter.Received!);
    }

    [Fact]
    public void Ignores_base_models_from_content_type_base_providers()
    {
        var options = Build(
            [typeof(ArticlePage), typeof(SharedBaseModelPage)],
            services =>
            {
                services.AddSingleton<IContentTypeBaseProvider>(new FakeBaseProvider(typeof(SharedBaseModelPage)));
                services.AddSingleton<IContentTypeBaseProvider>(new FakeBaseProvider(null));
            });

        Assert.True(options.ComputedFields.ContainsKey(typeof(ArticlePage)));
        Assert.False(options.ComputedFields.ContainsKey(typeof(SharedBaseModelPage)));
    }

    [Fact]
    public void Registers_fields_the_sdk_can_filter_and_invoke()
    {
        var options = Build([typeof(ArticlePage)]);

        foreach (var definition in options.ComputedFields[typeof(ArticlePage)])
        {
            var method = MethodOf(definition);

            Assert.Equal(IndexingType.Queryable, definition.IndexingType);
            Assert.True(method.IsDefined(typeof(ExtensionAttribute), false));
            Assert.Equal([typeof(PageData), typeof(IServiceProvider)], method.GetParameters().Select(p => p.ParameterType));
            Assert.Equal(typeof(List<>), method.ReturnType.GetGenericTypeDefinition());
        }
    }

    // Exclusions

    [Fact]
    public void Skips_excluded_types_and_types_derived_from_them()
    {
        var options = Build(
            [typeof(ArticlePage), typeof(SpecialArticlePage), typeof(NewsPage)],
            services => services.ConfigureGraphConventions(conventions => conventions.ExcludeContentType<ArticlePage>()));

        Assert.False(options.ComputedFields.ContainsKey(typeof(ArticlePage)));
        Assert.False(options.ComputedFields.ContainsKey(typeof(SpecialArticlePage)));
        Assert.True(options.ComputedFields.ContainsKey(typeof(NewsPage)));
    }

    [Fact]
    public void Sees_exclusions_configured_after_the_call()
    {
        var services = Services([typeof(ArticlePage)]);
        services.AddPageCategoriesToGraph();
        services.ConfigureGraphConventions(conventions => conventions.ExcludeContentType<ArticlePage>());

        Assert.Empty(Resolve(services).ComputedFields);
    }

    [Fact]
    public void Excluding_a_derived_type_keeps_its_concrete_base()
    {
        var options = Build(
            [typeof(ArticlePage), typeof(SpecialArticlePage)],
            services => services.ConfigureGraphConventions(conventions => conventions.ExcludeContentType<SpecialArticlePage>()));

        Assert.True(options.ComputedFields.ContainsKey(typeof(ArticlePage)));
        Assert.False(options.ComputedFields.ContainsKey(typeof(SpecialArticlePage)));
    }

    [Fact]
    public void Include_filter_can_reject_a_type()
    {
        var options = Build(
            [typeof(ArticlePage), typeof(NewsPage)],
            includeType: type => type != typeof(NewsPage));

        Assert.True(options.ComputedFields.ContainsKey(typeof(ArticlePage)));
        Assert.False(options.ComputedFields.ContainsKey(typeof(NewsPage)));
    }

    [Fact]
    public void Rejecting_a_derived_type_also_leaves_out_its_concrete_base()
    {
        var options = Build(
            [typeof(ArticlePage), typeof(SpecialArticlePage), typeof(NewsPage)],
            includeType: type => type != typeof(SpecialArticlePage));

        Assert.False(options.ComputedFields.ContainsKey(typeof(ArticlePage)));
        Assert.False(options.ComputedFields.ContainsKey(typeof(SpecialArticlePage)));
        Assert.True(options.ComputedFields.ContainsKey(typeof(NewsPage)));
    }

    // Duplicate registrations

    [Fact]
    public void Calling_twice_does_not_duplicate_fields()
    {
        var services = Services([typeof(ArticlePage)]);
        services.AddPageCategoriesToGraph();
        services.AddPageCategoriesToGraph();

        Assert.Equal([Ids, Names], FieldsOn(Resolve(services), typeof(ArticlePage)));
    }

    [Fact]
    public void Scanner_returning_a_type_twice_does_not_duplicate_fields()
    {
        var options = Build([typeof(ArticlePage), typeof(ArticlePage)]);

        Assert.Equal([Ids, Names], FieldsOn(options, typeof(ArticlePage)));
    }

    [Fact]
    public void Keeps_a_consumer_registration_of_the_same_field()
    {
        var options = Build(
            [typeof(ArticlePage)],
            services => services.ConfigureGraphConventions(conventions =>
                conventions.ForInstancesOf<ArticlePage>().IncludeField(page => page.LegacyCategoryIds())));

        var ids = Assert.Single(options.ComputedFields[typeof(ArticlePage)], definition => MethodOf(definition).Name == Ids);

        Assert.Equal(typeof(ConsumerFields), MethodOf(ids).DeclaringType);
        Assert.Contains(Names, FieldsOn(options, typeof(ArticlePage)));
    }

    [Fact]
    public void Does_not_override_a_consumer_field_inherited_from_a_base_type()
    {
        var options = Build(
            [typeof(ArticlePage), typeof(SpecialArticlePage)],
            services => services.ConfigureGraphConventions(conventions =>
                conventions.ForInstancesOf<ArticlePage>().IncludeField(page => page.LegacyCategoryIds())));

        Assert.Equal([Names], FieldsOn(options, typeof(SpecialArticlePage)));
    }

    [Fact]
    public void Does_not_override_a_consumer_field_from_a_contract()
    {
        var options = Build(
            [typeof(ContractPage)],
            services => services.ConfigureGraphConventions(conventions =>
                conventions.ForInstancesOf<IPageContract>().IncludeField(content => content.LegacyCategoryIds())));

        Assert.Equal([Names], FieldsOn(options, typeof(ContractPage)));
    }

    [Fact]
    public void Keeps_unrelated_consumer_fields()
    {
        var options = Build(
            [typeof(ArticlePage)],
            services => services.ConfigureGraphConventions(conventions =>
                conventions.ForInstancesOf<ArticlePage>().IncludeField(page => page.Summary())));

        Assert.Equal(["Summary", Ids, Names], FieldsOn(options, typeof(ArticlePage)));
    }

    // Field-name conflicts

    [Fact]
    public void Skips_only_the_field_that_conflicts_with_a_property()
    {
        var options = Build([typeof(ConflictingIdsPage)]);

        Assert.Equal([Names], FieldsOn(options, typeof(ConflictingIdsPage)));
    }

    [Fact]
    public void Compares_property_names_case_insensitively()
    {
        var options = Build([typeof(ConflictingCasePage)]);

        Assert.Equal([Ids], FieldsOn(options, typeof(ConflictingCasePage)));
    }

    [Fact]
    public void A_conflict_on_a_derived_type_also_removes_the_field_from_its_concrete_base()
    {
        var options = Build([typeof(ConflictingParentPage), typeof(ConflictingChildPage)]);

        Assert.Equal([Ids], FieldsOn(options, typeof(ConflictingParentPage)));
        Assert.Equal([Ids], FieldsOn(options, typeof(ConflictingChildPage)));
    }

    // Field values

    [Fact]
    public void Returns_ids_and_names_of_assigned_categories()
    {
        var page = PageWithCategories(1, 2);
        var services = CategoryServices(new Category { ID = 1, Name = "News" }, new Category { ID = 2, Name = "Sport" });

        Assert.Equal([1, 2], page.LegacyCategoryIds(services));
        Assert.Equal(["News", "Sport"], page.LegacyCategoryNames(services));
    }

    [Fact]
    public void Omits_categories_that_no_longer_exist_from_both_fields()
    {
        var page = PageWithCategories(1, 99);
        var services = CategoryServices(new Category { ID = 1, Name = "News" });

        Assert.Equal([1], page.LegacyCategoryIds(services));
        Assert.Equal(["News"], page.LegacyCategoryNames(services));
    }

    [Fact]
    public void Returns_empty_lists_when_the_page_has_no_categories()
    {
        var services = CategoryServices();

        Assert.Empty(new PageData().LegacyCategoryIds(services));
        Assert.Empty(PageWithCategories().LegacyCategoryNames(services));
    }

    private static GraphConventionOptions Build(
        Type[] scanned,
        Action<IServiceCollection>? configure = null,
        Func<Type, bool>? includeType = null)
    {
        var services = Services(scanned);
        configure?.Invoke(services);
        services.AddPageCategoriesToGraph(includeType);
        return Resolve(services);
    }

    private static ServiceCollection Services(Type[] scanned)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITypeScannerLookup>(new FakeTypeScanner(scanned));
        return services;
    }

    private static GraphConventionOptions Resolve(IServiceCollection services)
        => services.BuildServiceProvider().GetRequiredService<IOptions<GraphConventionOptions>>().Value;

    private static string[] FieldsOn(GraphConventionOptions options, Type type)
        => options.ComputedFields[type].Select(definition => MethodOf(definition).Name).ToArray();

    private static MethodInfo MethodOf(GraphComputedFieldDefinition definition)
    {
        var body = definition.Body is UnaryExpression unary ? unary.Operand : definition.Body;
        return ((MethodCallExpression)body).Method;
    }

    private static PageData PageWithCategories(params int[] ids)
    {
        var page = new PageData();
        page.Property.Add("PageCategory", new PropertyCategory(new CategoryList(ids)));
        return page;
    }

    private static ServiceProvider CategoryServices(params Category[] categories)
        => new ServiceCollection()
            .AddSingleton<CategoryRepository>(new FakeCategoryRepository(categories))
            .BuildServiceProvider();
}
