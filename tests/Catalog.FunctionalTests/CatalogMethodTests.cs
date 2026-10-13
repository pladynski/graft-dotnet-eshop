using eShop.Catalog.API;

namespace eShop.Catalog.FunctionalTests;

public sealed class CatalogMethodTests : IClassFixture<CatalogApiFixture>
{
    public CatalogMethodTests(CatalogApiFixture fixture)
    {
        _ = fixture.Services;
        CatalogApi.Attach(fixture.Services);
    }

    [Fact]
    public void ListItemsRespectsPageSize()
    {
        var page = CatalogApi.ListItems(0, 5, "", [], []);

        Assert.Equal(5, page.ItemCount);
        Assert.Equal(0, page.PageIndex);
        Assert.Equal(5, page.PageSize);
    }

    [Fact]
    public void UpdateItemWithoutPriceChangePersistsStock()
    {
        var item = Editable(CatalogApi.GetItem(1));
        var priorStock = item.AvailableStock;
        item.AvailableStock -= 1;

        var updated = CatalogApi.UpdateItem(item.Id, item);
        var saved = Item(CatalogApi.GetItem(1));

        Assert.Equal("updated", updated.Status);
        Assert.Equal(item.Id, saved.Id);
        Assert.NotEqual(priorStock, saved.AvailableStock);
    }

    [Fact]
    public void UpdateItemWithPriceChangePersistsPriceAndStock()
    {
        var item = Editable(CatalogApi.GetItem(1));
        var priorStock = item.AvailableStock;
        item.AvailableStock -= 1;
        item.Price = 1.99;

        var updated = CatalogApi.UpdateItem(item.Id, item);
        var saved = Item(CatalogApi.GetItem(1));

        Assert.Equal("updated", updated.Status);
        Assert.Equal(item.Id, saved.Id);
        Assert.Equal(1.99, saved.Price, 2);
        Assert.NotEqual(priorStock, saved.AvailableStock);
    }

    [Fact]
    public void GetItemsByIdsReturnsEachRequestedItem()
    {
        var items = CatalogApi.GetItemsByIds([1, 2, 3]);

        Assert.Equal(3, items.Count);
        Assert.Equal(3, items.Items.Length);
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
        var page = CatalogApi.ListItems(0, 5, "Wanderer Black Hiking Boots", [], []);

        Assert.Equal(1, page.ItemCount);
        Assert.Equal(1, page.Count);
        Assert.Equal(0, page.PageIndex);
        Assert.Equal(5, page.PageSize);
        Assert.Equal("Wanderer Black Hiking Boots", page.Items[0].Name);
    }

    [Fact]
    public void ListItemsFiltersByPartialName()
    {
        var page = CatalogApi.ListItems(0, 5, "Alpine", [], []);

        Assert.Equal(4, page.Count);
        Assert.Equal(0, page.PageIndex);
        Assert.Equal(5, page.PageSize);
        Assert.Contains("Alpine", page.Items[0].Name);
    }

    [Fact]
    public void GetItemPictureReturnsWebpBytes()
    {
        var picture = CatalogApi.GetItemPicture(1);

        Assert.Equal("image/webp", picture.Mime);
        Assert.False(string.IsNullOrEmpty(picture.Base64));
    }

    [Fact]
    public void SearchFallsBackToNameWhenEmbeddingsAreOff()
    {
        var page = CatalogApi.Search(0, 5, "Wanderer");

        Assert.Equal(1, page.Count);
        Assert.Equal(1, page.ItemCount);
        Assert.Equal(0, page.PageIndex);
        Assert.Equal(5, page.PageSize);
    }

    [Fact]
    public void ListItemsFiltersByTypeAndBrand()
    {
        var page = CatalogApi.ListItems(0, 5, "", [3], [3]);
        var first = page.Items[0];

        Assert.Equal(4, page.Count);
        Assert.Equal(0, page.PageIndex);
        Assert.Equal(5, page.PageSize);
        Assert.Equal(3, first.CatalogTypeId);
        Assert.Equal(3, first.CatalogBrandId);
    }

    [Fact]
    public void ListItemsFiltersByBrand()
    {
        var page = CatalogApi.ListItems(0, 5, "", [], [3]);

        Assert.Equal(11, page.Count);
        Assert.Equal(0, page.PageIndex);
        Assert.Equal(5, page.PageSize);
        Assert.Equal(3, page.Items[0].CatalogBrandId);
    }

    [Fact]
    public void ListTypesReturnsSeededTypes()
    {
        var types = CatalogApi.ListTypes();

        Assert.Equal(8, types.Count);
        Assert.Equal(8, types.Items.Length);
    }

    [Fact]
    public void ListBrandsReturnsSeededBrands()
    {
        var brands = CatalogApi.ListBrands();

        Assert.Equal(13, brands.Count);
        Assert.Equal(13, brands.Items.Length);
    }

    [Fact]
    public void CreateItemThenGetItemReturnsTheNewId()
    {
        const int id = 10015;
        var body = new CatalogItemWrite
        {
            Id = id,
            Name = "TestCatalog1",
            Description = "Test catalog description 1",
            Price = 11000.08,
            CatalogTypeId = 8,
            CatalogBrandId = 13,
            AvailableStock = 100,
            RestockThreshold = 10,
            MaxStockThreshold = 200
        };

        var created = CatalogApi.CreateItem(body);
        var saved = Item(CatalogApi.GetItem(id));

        Assert.Equal("created", created.Status);
        Assert.Equal(id, created.Id);
        Assert.Equal(id, saved.Id);
    }

    [Fact]
    public void DeleteItemThenGetItemIsMissing()
    {
        const int id = 5;

        var deleted = CatalogApi.DeleteItem(id);

        Assert.Equal("deleted", deleted.Status);
        Assert.Equal(0, CatalogApi.GetItem(id).Found);
    }

    [Fact]
    public void GetItemRejectsInvalidId()
    {
        Assert.Equal(0, CatalogApi.GetItem(0).Found);
    }

    [Fact]
    public void GetAndDeleteMissingItemReturnNotFound()
    {
        const int missingId = int.MaxValue;

        Assert.Equal(0, CatalogApi.GetItem(missingId).Found);
        Assert.Equal("notFound", CatalogApi.DeleteItem(missingId).Status);
    }

    [Fact]
    public void ListItemsPastTheLastPageReturnsEmptyData()
    {
        var page = CatalogApi.ListItems(1000, 10, "", [], []);

        Assert.Equal(0, page.ItemCount);
        Assert.Empty(page.Items);
        Assert.Equal(1000, page.PageIndex);
    }

    private static CatalogItemView Item(CatalogItemHit hit) =>
        hit.Found == 1 && hit.Item is not null
            ? hit.Item
            : throw new InvalidOperationException("Catalog item was missing.");

    private static CatalogItemWrite Editable(CatalogItemHit hit)
    {
        var view = Item(hit);
        return new CatalogItemWrite
        {
            Id = view.Id,
            Name = view.Name,
            Description = view.Description,
            Price = view.Price,
            PictureFileName = view.PictureFileName,
            CatalogBrandId = view.CatalogBrandId,
            CatalogTypeId = view.CatalogTypeId,
            AvailableStock = view.AvailableStock,
            RestockThreshold = view.RestockThreshold,
            MaxStockThreshold = view.MaxStockThreshold
        };
    }
}
