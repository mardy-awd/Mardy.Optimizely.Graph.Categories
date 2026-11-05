using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.DataAbstraction.RuntimeModel;
using EPiServer.Framework.TypeScanner;

namespace Mardy.Optimizely.Graph.Categories.Tests;

public abstract class AbstractPage : PageData;

public class ArticlePage : AbstractPage;

public class SpecialArticlePage : ArticlePage;

public class NewsPage : PageData;

public class OpenGenericPage<T> : PageData;

public class SharedBaseModelPage : PageData;

public class TeaserBlock : BlockData;

public interface IPageContract : IContentData;

public class ContractPage : PageData, IPageContract;

public class ConflictingIdsPage : PageData
{
    public virtual string? LegacyCategoryIds { get; set; }
}

public class ConflictingCasePage : PageData
{
    public virtual string? LEGACYCATEGORYNAMES { get; set; }
}

public class ConflictingParentPage : PageData;

public class ConflictingChildPage : ConflictingParentPage
{
    public virtual string? LegacyCategoryNames { get; set; }
}

public static class ConsumerFields
{
    public static string LegacyCategoryIds(this ArticlePage page) => page.Name;

    public static string LegacyCategoryIds(this IPageContract content) => string.Empty;

    public static string Summary(this ArticlePage page) => page.Name;
}

internal sealed class FakeTypeScanner(params Type[] types) : ITypeScannerLookup
{
    public IEnumerable<Type> AllTypes => types;
}

internal sealed class RemovingModelFilter(Type removed) : IContentTypeModelFilter
{
    public IList<Type>? Received { get; private set; }

    public IList<Type> Filter(IList<Type> types)
    {
        Received = types;
        return types.Where(type => type != removed).ToList();
    }
}

internal sealed class FakeBaseProvider(Type? model) : IContentTypeBaseProvider
{
    public IEnumerable<ContentTypeBase> ContentTypeBases => [ContentTypeBase.Page];

    public Type Resolve(ContentTypeBase contentTypeBase) => model!;
}

internal sealed class FakeCategoryRepository(params Category[] categories) : CategoryRepository
{
    public override Category Get(int id) => categories.FirstOrDefault(category => category.ID == id)!;

    public override Category Get(string name) => throw new NotSupportedException();

    public override Category GetRoot() => throw new NotSupportedException();

    public override void Save(Category category) => throw new NotSupportedException();

    public override void Delete(Category category) => throw new NotSupportedException();
}
