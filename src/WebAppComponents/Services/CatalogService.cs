// Graftcode catalog slice — same CatalogService, calls the generated Catalog graft.
using eShop.WebAppComponents.Catalog;
using graft.nuget.Catalog.API;
using CatalogApi = graft.nuget.eShop.Catalog.API.CatalogApi;
using GraftBrand = graft.nuget.eShop.Catalog.API.CatalogBrandView;
using GraftFacet = graft.nuget.eShop.Catalog.API.CatalogFacetSet;
using GraftItem = graft.nuget.eShop.Catalog.API.CatalogItemView;
using GraftPage = graft.nuget.eShop.Catalog.API.CatalogPage;

namespace eShop.WebAppComponents.Services;

public class CatalogService : ICatalogService
{
    public const string DefaultHost = "ws://localhost:8000/ws";

    static CatalogService() => Configure();

    public static void Configure()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("CATALOG_GRAFT_TRANSPORT"), "rabbitmq", StringComparison.OrdinalIgnoreCase))
        {
            GraftConfig.SetConfig(ReadPluginConfig());
            return;
        }

        var socket = Environment.GetEnvironmentVariable("CATALOG_GRAFT_HOST");
        GraftConfig.Host = string.IsNullOrWhiteSpace(socket) ? DefaultHost : socket.Trim();
        GraftConfig.Stateless = true;
    }

    public Task<CatalogItem?> GetCatalogItem(int id)
    {
        var hit = CatalogApi.GetItem(id);
        // gg 1.4.9 null complex return: do not read Item when Found is 0.
        if (hit.Found == 0)
        {
            return Task.FromResult<CatalogItem?>(null);
        }

        return Task.FromResult<CatalogItem?>(Map(hit.Item));
    }

    public Task<CatalogResult> GetCatalogItems(int pageIndex, int pageSize, int[]? brands, int[]? types) =>
        Task.FromResult(Map(CatalogApi.ListItems(pageIndex, pageSize, string.Empty, types ?? [], brands ?? [])));

    public Task<List<CatalogItem>> GetCatalogItems(IEnumerable<int> ids)
    {
        var set = CatalogApi.GetItemsByIds(ids?.ToArray() ?? []);
        // gg 1.4.9 empty complex array: do not read Items when Count is 0.
        var items = set.Count == 0 ? new List<CatalogItem>() : set.Items.Select(Map).ToList();
        return Task.FromResult(items);
    }

    public Task<CatalogResult> GetCatalogItemsWithSemanticRelevance(int page, int take, string text) =>
        Task.FromResult(Map(CatalogApi.Search(page, take, text ?? string.Empty)));

    public Task<IEnumerable<CatalogBrand>> GetBrands()
    {
        var brands = CatalogApi.ListBrands();
        // gg 1.4.9 empty complex array: do not read Items when Count is 0.
        IEnumerable<CatalogBrand> items = brands.Count == 0
            ? []
            : brands.Items.Select(brand => new CatalogBrand(brand.Id, brand.Brand ?? string.Empty));
        return Task.FromResult(items);
    }

    public Task<IEnumerable<CatalogItemType>> GetTypes()
    {
        var types = CatalogApi.ListTypes();
        // gg 1.4.9 empty complex array: do not read Items when Count is 0.
        IEnumerable<CatalogItemType> items = types.Count == 0
            ? []
            : types.Items.Select(type => new CatalogItemType(type.Id, type.Type ?? string.Empty));
        return Task.FromResult(items);
    }

    public Task<CatalogFacets> GetCatalogFacets(int[]? brands, int[]? types) =>
        Task.FromResult(Map(CatalogApi.GetFacets(types ?? [], brands ?? [])));

    public (string Mime, byte[] Bytes)? GetItemPicture(int id)
    {
        var picture = CatalogApi.GetItemPicture(id);
        if (string.IsNullOrEmpty(picture.Base64))
        {
            return null;
        }

        return (picture.Mime, Convert.FromBase64String(picture.Base64));
    }

    private static string ReadPluginConfig()
    {
        var path = Environment.GetEnvironmentVariable("CATALOG_GRAFT_PLUGIN_CONFIG");
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            return File.ReadAllText(path);
        }

        var pluginHost = Environment.GetEnvironmentVariable("CATALOG_GRAFT_PLUGIN_HOST");
        if (string.IsNullOrWhiteSpace(pluginHost))
        {
            pluginHost = "localhost:5672";
        }

        return $$"""
        {
          "configurations": {
            "graft.nuget.Catalog.API": {
              "runtime": "netcore",
              "host": "{{pluginHost.Trim()}}",
              "stateless": true,
              "plugin": {
                "name": "RabbitmqPlugin",
                "queue": "eshop.catalog",
                "replyQueue": "eshop.catalog.reply",
                "user": "guest",
                "password": "guest",
                "vhost": "/",
                "rpcTimeoutMs": 30000
              }
            }
          }
        }
        """;
    }

    private static CatalogResult Map(GraftPage? page)
    {
        if (page is null)
        {
            return new CatalogResult(0, 0, 0, []);
        }

        // gg 1.4.9 empty complex array: do not read Items when ItemCount is 0.
        var data = page.ItemCount == 0
            ? new List<CatalogItem>()
            : page.Items.Select(Map).ToList();
        return new CatalogResult(page.PageIndex, page.PageSize, page.Count, data);
    }

    private static CatalogFacets Map(GraftFacet? facets)
    {
        if (facets is null)
        {
            return new CatalogFacets([], [], 0, 0);
        }

        // gg 1.4.9 empty complex array: do not read the count arrays when the matching count is 0.
        var brands = facets.BrandCount == 0
            ? Array.Empty<CatalogFacetCount>()
            : facets.BrandCounts.Select(count => new CatalogFacetCount(count.Id, count.Count)).ToArray();
        var types = facets.TypeCount == 0
            ? Array.Empty<CatalogFacetCount>()
            : facets.TypeCounts.Select(count => new CatalogFacetCount(count.Id, count.Count)).ToArray();
        return new CatalogFacets(brands, types, facets.BrandTotal, facets.TypeTotal);
    }

    private static CatalogItem Map(GraftItem item)
    {
        var brand = item.Brand ?? new GraftBrand();
        var type = item.Type ?? new graft.nuget.eShop.Catalog.API.CatalogTypeView();
        return new CatalogItem(
            item.Id,
            item.Name ?? string.Empty,
            item.Description ?? string.Empty,
            (decimal)item.Price,
            string.Empty,
            item.CatalogBrandId,
            new CatalogBrand(brand.Id == 0 ? item.CatalogBrandId : brand.Id, brand.Brand ?? string.Empty),
            item.CatalogTypeId,
            new CatalogItemType(type.Id == 0 ? item.CatalogTypeId : type.Id, type.Type ?? string.Empty));
    }

}
