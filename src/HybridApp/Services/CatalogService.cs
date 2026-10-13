using eShop.WebAppComponents.Catalog;
using eShop.WebAppComponents.Services;
using GraftCatalog = eShop.WebAppComponents.Services.CatalogService;

namespace eShop.HybridApp.Services;

// Same catalog reads as the mobile BFF HTTP client. Anonymous. The call is synchronous on the graft.
public class CatalogService(GraftCatalog catalog) : ICatalogService
{
    public Task<CatalogItem?> GetCatalogItem(int id) => catalog.GetCatalogItem(id);

    public Task<CatalogResult> GetCatalogItems(int pageIndex, int pageSize, int[]? brands, int[]? types) =>
        catalog.GetCatalogItems(pageIndex, pageSize, brands, types);

    public Task<List<CatalogItem>> GetCatalogItems(IEnumerable<int> ids) => catalog.GetCatalogItems(ids);

    public Task<CatalogResult> GetCatalogItemsWithSemanticRelevance(int page, int take, string text) =>
        catalog.GetCatalogItemsWithSemanticRelevance(page, take, text);

    public Task<IEnumerable<CatalogBrand>> GetBrands() => catalog.GetBrands();

    public Task<IEnumerable<CatalogItemType>> GetTypes() => catalog.GetTypes();
}
