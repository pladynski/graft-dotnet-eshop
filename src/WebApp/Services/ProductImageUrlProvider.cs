// Graftcode catalog slice — picture URL is the web app file endpoint, not catalog HTTP.
using eShop.WebAppComponents.Services;

namespace eShop.WebApp.Services;

public class ProductImageUrlProvider : IProductImageUrlProvider
{
    public string GetProductImageUrl(int productId)
        => $"product-images/{productId}";
}
