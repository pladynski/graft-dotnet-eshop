// Graftcode basket slice — same BasketService, calls the generated Basket graft.
// The access token is the one OpenIdConnect saved (SaveTokens), the same token AddAuthToken sends.
// InvokeWithHeaders binds it to this call. It is not stored in GraftConfig.SetHeaders.
using graft.nuget.Basket.API;
using Microsoft.AspNetCore.Authentication;
using BasketApi = graft.nuget.eShop.Basket.API.BasketService;
using BasketResult = graft.nuget.eShop.Basket.API.BasketResult;

namespace eShop.WebApp.Services;

public class BasketService(IHttpContextAccessor httpContextAccessor)
{
    public const string DefaultHost = "ws://localhost:8000/ws";

    static BasketService() => Configure();

    public static void Configure()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("BASKET_GRAFT_TRANSPORT"), "rabbitmq", StringComparison.OrdinalIgnoreCase))
        {
            GraftConfig.SetConfig(ReadPluginConfig());
            return;
        }

        var socket = Environment.GetEnvironmentVariable("BASKET_GRAFT_HOST");
        GraftConfig.Host = string.IsNullOrWhiteSpace(socket) ? DefaultHost : socket.Trim();
        GraftConfig.Stateless = true;
    }

    public async Task<IReadOnlyCollection<BasketQuantity>> GetBasketAsync()
    {
        var result = await InvokeAsync(() => BasketApi.GetBasket());
        ThrowIfUnauthenticated(result);
        return Lines(result);
    }

    public async Task DeleteBasketAsync()
    {
        ThrowIfUnauthenticated(await InvokeAsync(() => BasketApi.DeleteBasket()));
    }

    public async Task UpdateBasketAsync(IReadOnlyCollection<BasketQuantity> basket)
    {
        var lines = basket ?? [];
        ThrowIfUnauthenticated(await InvokeAsync(() => BasketApi.UpdateBasket(
            lines.Select(item => item.ProductId).ToArray(),
            lines.Select(item => item.Quantity).ToArray())));
    }

    private async Task<BasketResult> InvokeAsync(Func<BasketResult> call)
    {
        var headers = new Dictionary<string, string>();
        var context = httpContextAccessor.HttpContext;
        if (context is not null)
        {
            var accessToken = await context.GetTokenAsync("access_token");
            if (!string.IsNullOrEmpty(accessToken))
            {
                headers["Authorization"] = "Bearer " + accessToken;
            }
        }

        return GraftConfig.InvokeWithHeaders(call, headers);
    }

    private static IReadOnlyCollection<BasketQuantity> Lines(BasketResult result)
    {
        if (result is null || result.Count <= 0)
        {
            return [];
        }

        var ids = result.ProductIds;
        var quantities = result.Quantities;
        if (ids is null || quantities is null)
        {
            return [];
        }

        var count = Math.Min(result.Count, Math.Min(ids.Length, quantities.Length));
        var lines = new List<BasketQuantity>(count);
        for (var i = 0; i < count; i++)
        {
            lines.Add(new BasketQuantity(ids[i], quantities[i]));
        }

        return lines;
    }

    private static void ThrowIfUnauthenticated(BasketResult result)
    {
        if (string.Equals(result?.Status, "unauthenticated", StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("You must be logged in.");
        }
    }

    private static string ReadPluginConfig()
    {
        var path = Environment.GetEnvironmentVariable("BASKET_GRAFT_PLUGIN_CONFIG");
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            return File.ReadAllText(path);
        }

        var pluginHost = Environment.GetEnvironmentVariable("BASKET_GRAFT_PLUGIN_HOST");
        if (string.IsNullOrWhiteSpace(pluginHost))
        {
            pluginHost = "localhost:5672";
        }

        return $$"""
        {
          "configurations": {
            "graft.nuget.Basket.API": {
              "runtime": "netcore",
              "host": "{{pluginHost.Trim()}}",
              "stateless": true,
              "plugin": {
                "name": "RabbitmqPlugin",
                "queue": "eshop.basket",
                "replyQueue": "eshop.basket.reply",
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
}

public record BasketQuantity(int ProductId, int Quantity);
