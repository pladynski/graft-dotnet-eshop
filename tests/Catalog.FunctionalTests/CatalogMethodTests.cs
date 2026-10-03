using System.Text.Json;
using eShop.Catalog.API;
using eShop.Catalog.API.Model;

namespace eShop.Catalog.FunctionalTests;

public sealed class CatalogMethodTests : IClassFixture<CatalogApiFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public CatalogMethodTests(CatalogApiFixture fixture)
    {
        _ = fixture.Services;
        CatalogApi.Attach(fixture.Services);
    }

    [Fact]
    public void ListItemsRespectsPageSize()
    {
        var page = Page(CatalogApi.ListItems(0, 5, "", "", ""));

        Assert.Equal(5, page.Data.Count());
        Assert.Equal(0, page.PageIndex);
        Assert.Equal(5, page.PageSize);
    }

    [Fact]
    public void UpdateItemWithoutPriceChangePersistsStock()
    {
        var item = Item(CatalogApi.GetItem(1));
        var priorStock = item.AvailableStock;
        item.AvailableStock -= 1;

        var updated = Status(CatalogApi.UpdateItem(item.Id, JsonSerializer.Serialize(item, Json)));
        var saved = Item(CatalogApi.GetItem(1));

        Assert.Equal("updated", updated);
        Assert.Equal(item.Id, saved.Id);
        Assert.NotEqual(priorStock, saved.AvailableStock);
    }

    [Fact]
    public void UpdateItemWithPriceChangePersistsPriceAndStock()
    {
        var item = Item(CatalogApi.GetItem(1));
        var priorStock = item.AvailableStock;
        item.AvailableStock -= 1;
        item.Price = 1.99m;

        var updated = Status(CatalogApi.UpdateItem(item.Id, JsonSerializer.Serialize(item, Json)));
        var saved = Item(CatalogApi.GetItem(1));

        Assert.Equal("updated", updated);
        Assert.Equal(item.Id, saved.Id);
        Assert.Equal(1.99m, saved.Price);
        Assert.NotEqual(priorStock, saved.AvailableStock);
    }

    [Fact]
    public void GetItemsByIdsReturnsEachRequestedItem()
    {
        var items = JsonSerializer.Deserialize<List<CatalogItem>>(CatalogApi.GetItemsByIds("1,2,3"), Json);

        Assert.NotNull(items);
        Assert.Equal(3, items.Count);
    }

    [Fact]
    public void GetItemReturnsTheRequestedItem()
    {
        var item = Item(CatalogApi.GetItem(2));

        Assert.Equal(2, item.Id);
    }

    [Fact]
    public void ListItemsFiltersByExactName()
    {
        var page = Page(CatalogApi.ListItems(0, 5, "Wanderer Black Hiking Boots", "", ""));

        Assert.NotNull(page.Data);
        Assert.Equal(1, page.Count);
        Assert.Equal(0, page.PageIndex);
        Assert.Equal(5, page.PageSize);
        Assert.Equal("Wanderer Black Hiking Boots", page.Data.First().Name);
    }

    [Fact]
    public void ListItemsFiltersByPartialName()
    {
        var page = Page(CatalogApi.ListItems(0, 5, "Alpine", "", ""));

        Assert.NotNull(page.Data);
        Assert.Equal(4, page.Count);
        Assert.Equal(0, page.PageIndex);
        Assert.Equal(5, page.PageSize);
        Assert.Contains("Alpine", page.Data.First().Name);
    }

    [Fact]
    public void GetItemPictureReturnsWebpBytes()
    {
        using var document = JsonDocument.Parse(CatalogApi.GetItemPicture(1));

        Assert.Equal("image/webp", document.RootElement.GetProperty("mime").GetString());
        Assert.False(string.IsNullOrEmpty(document.RootElement.GetProperty("base64").GetString()));
    }

    [Fact]
    public void SearchFallsBackToNameWhenEmbeddingsAreOff()
    {
        var page = Page(CatalogApi.Search(0, 5, "Wanderer"));

        Assert.Equal(1, page.Count);
        Assert.NotNull(page.Data);
        Assert.Equal(0, page.PageIndex);
        Assert.Equal(5, page.PageSize);
    }

    [Fact]
    public void ListItemsFiltersByTypeAndBrand()
    {
        var page = Page(CatalogApi.ListItems(0, 5, "", "3", "3"));
        var first = page.Data.First();

        Assert.Equal(4, page.Count);
        Assert.Equal(0, page.PageIndex);
        Assert.Equal(5, page.PageSize);
        Assert.Equal(3, first.CatalogTypeId);
        Assert.Equal(3, first.CatalogBrandId);
    }

    [Fact]
    public void ListItemsFiltersByBrand()
    {
        var page = Page(CatalogApi.ListItems(0, 5, "", "", "3"));

        Assert.Equal(11, page.Count);
        Assert.Equal(0, page.PageIndex);
        Assert.Equal(5, page.PageSize);
        Assert.Equal(3, page.Data.First().CatalogBrandId);
    }

    [Fact]
    public void ListTypesReturnsSeededTypes()
    {
        var types = JsonSerializer.Deserialize<List<CatalogType>>(CatalogApi.ListTypes(), Json);

        Assert.NotNull(types);
        Assert.Equal(8, types.Count);
    }

    [Fact]
    public void ListBrandsReturnsSeededBrands()
    {
        var brands = JsonSerializer.Deserialize<List<CatalogBrand>>(CatalogApi.ListBrands(), Json);

        Assert.NotNull(brands);
        Assert.Equal(13, brands.Count);
    }

    [Fact]
    public void CreateItemThenGetItemReturnsTheNewId()
    {
        const int id = 10015;
        var body = new CatalogItem("TestCatalog1")
        {
            Id = id,
            Description = "Test catalog description 1",
            Price = 11000.08m,
            CatalogTypeId = 8,
            CatalogBrandId = 13,
            AvailableStock = 100,
            RestockThreshold = 10,
            MaxStockThreshold = 200
        };

        using var created = JsonDocument.Parse(CatalogApi.CreateItem(JsonSerializer.Serialize(body, Json)));
        var saved = Item(CatalogApi.GetItem(id));

        Assert.Equal(id, created.RootElement.GetProperty("id").GetInt32());
        Assert.Equal(id, saved.Id);
    }

    [Fact]
    public void DeleteItemThenGetItemIsMissing()
    {
        const int id = 5;

        var deleted = Status(CatalogApi.DeleteItem(id));

        Assert.Equal("deleted", deleted);
        Assert.Equal("null", CatalogApi.GetItem(id));
    }

    [Fact]
    public void GetItemRejectsInvalidId()
    {
        Assert.Equal("null", CatalogApi.GetItem(0));
    }

    [Fact]
    public void GetAndDeleteMissingItemReturnNotFound()
    {
        const int missingId = int.MaxValue;

        Assert.Equal("null", CatalogApi.GetItem(missingId));
        Assert.Equal("notFound", Status(CatalogApi.DeleteItem(missingId)));
    }

    [Fact]
    public void ListItemsPastTheLastPageReturnsEmptyData()
    {
        var page = Page(CatalogApi.ListItems(1000, 10, "", "", ""));

        Assert.Empty(page.Data);
        Assert.Equal(1000, page.PageIndex);
    }

    private static PaginatedItems<CatalogItem> Page(string json) =>
        JsonSerializer.Deserialize<PaginatedItems<CatalogItem>>(json, Json)
        ?? throw new InvalidOperationException("Catalog page JSON was empty.");

    private static CatalogItem Item(string json) =>
        JsonSerializer.Deserialize<CatalogItem>(json, Json)
        ?? throw new InvalidOperationException("Catalog item JSON was empty.");

    private static string Status(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("status").GetString()
            ?? throw new InvalidOperationException("Status JSON was empty.");
    }
}
