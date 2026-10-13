using eShop.WebAppComponents.Services;
using GraftCatalog = eShop.WebAppComponents.Services.CatalogService;

namespace eShop.HybridApp.Services;

// The BFF /api/catalog/items/{id}/pic route is gone. The bytes are the same anonymous catalog read.
public class ProductImageUrlProvider(GraftCatalog catalog) : IProductImageUrlProvider
{
    public string GetProductImageUrl(int productId)
    {
        try
        {
            var picture = catalog.GetItemPicture(productId);
            if (picture is null || picture.Value.Bytes is null || picture.Value.Bytes.Length == 0)
            {
                return string.Empty;
            }

            var mime = string.IsNullOrWhiteSpace(picture.Value.Mime) ? "application/octet-stream" : picture.Value.Mime;
            return $"data:{mime};base64,{Convert.ToBase64String(picture.Value.Bytes)}";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex.Message);
            return string.Empty;
        }
    }
}
