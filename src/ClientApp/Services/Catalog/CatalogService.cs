using eShop.ClientApp.Models.Catalog;
using eShop.ClientApp.Services.FixUri;
using eShop.ClientApp.Services.Settings;
using graft.nuget.Catalog.API;
using CatalogApi = graft.nuget.eShop.Catalog.API.CatalogApi;
using GraftBrand = graft.nuget.eShop.Catalog.API.CatalogBrandView;
using GraftItem = graft.nuget.eShop.Catalog.API.CatalogItemView;
using GraftPage = graft.nuget.eShop.Catalog.API.CatalogPage;

namespace eShop.ClientApp.Services.Catalog;

// Same catalog reads as the HTTP client. Anonymous. Synchronous graft call, returned as a task.
public class CatalogService : ICatalogService
{
    private readonly IFixUriService _fixUriService;
    private readonly ISettingsService _settingsService;

    public CatalogService(ISettingsService settingsService, IFixUriService fixUriService)
    {
        _settingsService = settingsService;
        _fixUriService = fixUriService;
    }

    public Task<IEnumerable<CatalogItem>> FilterAsync(int catalogBrandId, int catalogTypeId)
    {
        UseHost();
        var page = CatalogApi.ListItems(0, 100, string.Empty, [catalogTypeId], [catalogBrandId]);
        return Task.FromResult(Materialize(page));
    }

    public Task<IEnumerable<CatalogItem>> GetCatalogAsync()
    {
        UseHost();
        var page = CatalogApi.ListItems(0, 100, string.Empty, [], []);
        return Task.FromResult(Materialize(page));
    }

    public Task<CatalogItem> GetCatalogItemAsync(int catalogItemId)
    {
        UseHost();
        var hit = CatalogApi.GetItem(catalogItemId);
        // gg 1.4.9 null complex return: do not read Item when Found is 0.
        if (hit is null || hit.Found == 0)
        {
            return Task.FromResult<CatalogItem>(default);
        }

        var item = Map(hit.Item);
        ApplyPicture(item);
        _fixUriService.FixCatalogItemPictureUri([item]);
        return Task.FromResult(item);
    }

    public Task<IEnumerable<CatalogBrand>> GetCatalogBrandAsync()
    {
        UseHost();
        var brands = CatalogApi.ListBrands();
        // gg 1.4.9 empty complex array: do not read Items when Count is 0.
        IEnumerable<CatalogBrand> items = brands is null || brands.Count == 0
            ? []
            : brands.Items.Select(brand => new CatalogBrand { Id = brand.Id, Brand = brand.Brand ?? string.Empty });
        return Task.FromResult(items);
    }

    public Task<IEnumerable<CatalogType>> GetCatalogTypeAsync()
    {
        UseHost();
        var types = CatalogApi.ListTypes();
        IEnumerable<CatalogType> items = types is null || types.Count == 0
            ? []
            : types.Items.Select(type => new CatalogType { Id = type.Id, Type = type.Type ?? string.Empty });
        return Task.FromResult(items);
    }

    private IEnumerable<CatalogItem> Materialize(GraftPage page)
    {
        if (page is null || page.ItemCount == 0)
        {
            return [];
        }

        var items = page.Items.Select(Map).ToList();
        foreach (var item in items)
        {
            ApplyPicture(item);
        }

        _fixUriService.FixCatalogItemPictureUri(items);
        return items;
    }

    private static CatalogItem Map(GraftItem item)
    {
        var brand = item.Brand ?? new GraftBrand();
        var type = item.Type ?? new graft.nuget.eShop.Catalog.API.CatalogTypeView();
        return new CatalogItem
        {
            Id = item.Id,
            Name = item.Name ?? string.Empty,
            Description = item.Description ?? string.Empty,
            Price = (decimal)item.Price,
            CatalogBrandId = item.CatalogBrandId,
            CatalogBrand = new CatalogBrand
            {
                Id = brand.Id == 0 ? item.CatalogBrandId : brand.Id,
                Brand = brand.Brand ?? string.Empty
            },
            CatalogTypeId = item.CatalogTypeId,
            CatalogType = new CatalogType
            {
                Id = type.Id == 0 ? item.CatalogTypeId : type.Id,
                Type = type.Type ?? string.Empty
            }
        };
    }

    // The BFF picture URL is gone. The image bytes are the same anonymous catalog read, saved
    // where the MAUI image control can load a file path.
    private static void ApplyPicture(CatalogItem item)
    {
        if (item is null || item.Id <= 0)
        {
            return;
        }

        try
        {
            var picture = CatalogApi.GetItemPicture(item.Id);
            if (picture is null || string.IsNullOrEmpty(picture.Base64))
            {
                return;
            }

            var mime = picture.Mime ?? string.Empty;
            var extension = mime.Contains("png", StringComparison.OrdinalIgnoreCase) ? "png"
                : mime.Contains("jpeg", StringComparison.OrdinalIgnoreCase) || mime.Contains("jpg", StringComparison.OrdinalIgnoreCase) ? "jpg"
                : "webp";
            var path = Path.Combine(FileSystem.CacheDirectory, $"catalog-{item.Id}.{extension}");
            File.WriteAllBytes(path, Convert.FromBase64String(picture.Base64));
            item.PictureUri = path;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex.Message);
        }
    }

    private void UseHost()
    {
        GraftConfig.Host = GraftHosts.Socket(_settingsService.GatewayCatalogEndpointBase);
        GraftConfig.Stateless = true;
    }
}
