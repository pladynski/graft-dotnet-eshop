using eShop.ClientApp.Exceptions;
using eShop.ClientApp.Models.Basket;
using eShop.ClientApp.Services.FixUri;
using eShop.ClientApp.Services.Identity;
using eShop.ClientApp.Services.Settings;
using graft.nuget.Basket.API;
using BasketApi = graft.nuget.eShop.Basket.API.BasketService;
using BasketResult = graft.nuget.eShop.Basket.API.BasketResult;
using BasketItem = eShop.ClientApp.Models.Basket.BasketItem;

namespace eShop.ClientApp.Services.Basket;

// Same basket operations as the gRPC client. The user access token is sent on that call only.
public class BasketService : IBasketService
{
    private readonly IFixUriService _fixUriService;
    private readonly IIdentityService _identityService;
    private readonly ISettingsService _settingsService;

    public BasketService(IIdentityService identityService, ISettingsService settingsService,
        IFixUriService fixUriService)
    {
        _identityService = identityService;
        _settingsService = settingsService;
        _fixUriService = fixUriService;
    }

    public IEnumerable<BasketItem> LocalBasketItems { get; set; }

    public async Task<CustomerBasket> GetBasketAsync()
    {
        CustomerBasket basket = new();

        var authToken = await _identityService.GetAuthTokenAsync().ConfigureAwait(false);

        if (string.IsNullOrEmpty(authToken))
        {
            return basket;
        }

        try
        {
            var basketResponse = Invoke(authToken, () => BasketApi.GetBasket());
            if (basketResponse is null || !string.Equals(basketResponse.Status, "ok", StringComparison.Ordinal))
            {
                Console.WriteLine(basketResponse is null ? "basket failed" : basketResponse.Status + " " + basketResponse.Detail);
                return null;
            }

            if (basketResponse.Count > 0)
            {
                var ids = basketResponse.ProductIds;
                var quantities = basketResponse.Quantities;
                var count = Math.Min(basketResponse.Count, Math.Min(ids.Length, quantities.Length));
                for (var i = 0; i < count; i++)
                {
                    basket.AddItemToBasket(new BasketItem { ProductId = ids[i], Quantity = quantities[i] });
                }
            }
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
            basket = null;
        }

        _fixUriService.FixBasketItemPictureUri(basket?.Items);
        return basket;
    }

    public async Task<CustomerBasket> UpdateBasketAsync(CustomerBasket customerBasket)
    {
        var authToken = await _identityService.GetAuthTokenAsync().ConfigureAwait(false);

        if (string.IsNullOrEmpty(authToken))
        {
            return customerBasket;
        }

        var items = customerBasket?.Items?.ToArray() ?? [];
        var result = Invoke(authToken, () => BasketApi.UpdateBasket(
            items.Select(item => item.ProductId).ToArray(),
            items.Select(item => item.Quantity).ToArray()));
        ThrowIfRejected(result);

        if (result.Count > 0)
        {
            customerBasket.ClearBasket();
            var ids = result.ProductIds;
            var quantities = result.Quantities;
            var count = Math.Min(result.Count, Math.Min(ids.Length, quantities.Length));
            for (var i = 0; i < count; i++)
            {
                customerBasket.AddItemToBasket(new BasketItem { ProductId = ids[i], Quantity = quantities[i] });
            }
        }

        return customerBasket;
    }

    public async Task ClearBasketAsync()
    {
        var authToken = await _identityService.GetAuthTokenAsync().ConfigureAwait(false);

        if (string.IsNullOrEmpty(authToken))
        {
            return;
        }

        var result = Invoke(authToken, () => BasketApi.DeleteBasket());
        ThrowIfRejected(result);
    }

    private void UseHost()
    {
        GraftConfig.Host = GraftHosts.Socket(_settingsService.GatewayBasketEndpointBase);
        GraftConfig.Stateless = true;
    }

    private BasketResult Invoke(string token, Func<BasketResult> call)
    {
        UseHost();
        return GraftConfig.InvokeWithHeaders(call, new Dictionary<string, string>
        {
            ["Authorization"] = "Bearer " + token
        });
    }

    private static void ThrowIfRejected(BasketResult result)
    {
        if (result is null)
        {
            throw new ServiceAuthenticationException("Basket call failed.");
        }

        if (string.Equals(result.Status, "unauthenticated", StringComparison.Ordinal)
            || string.Equals(result.Status, "forbidden", StringComparison.Ordinal))
        {
            throw new ServiceAuthenticationException(result.Detail ?? result.Status);
        }

        if (!string.Equals(result.Status, "ok", StringComparison.Ordinal)
            && !string.Equals(result.Status, "deleted", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(result.Detail ?? result.Status);
        }
    }
}
