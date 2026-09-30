// Graftcode basket slice — same BasketService, calls the generated Basket graft.
using graft.nuget.Basket.API;
using Microsoft.AspNetCore.Components.Authorization;
using BasketApi = graft.nuget.eShop.Basket.API.BasketService;
using BasketResult = graft.nuget.eShop.Basket.API.BasketResult;

namespace eShop.WebApp.Services;

public class BasketService(AuthenticationStateProvider authenticationStateProvider)
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
        var result = BasketApi.GetBasket(await BuyerIdAsync());
        return Lines(result);
    }

    public async Task DeleteBasketAsync()
    {
        ThrowIfUnauthenticated(BasketApi.DeleteBasket(await BuyerIdAsync()));
    }

    public async Task UpdateBasketAsync(IReadOnlyCollection<BasketQuantity> basket)
    {
        var lines = basket ?? [];
        ThrowIfUnauthenticated(BasketApi.UpdateBasket(
            await BuyerIdAsync(),
            lines.Select(item => item.ProductId).ToArray(),
            lines.Select(item => item.Quantity).ToArray()));
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

    private async Task<string> BuyerIdAsync()
    {
        var user = (await authenticationStateProvider.GetAuthenticationStateAsync()).User;
        return user.FindFirst("sub")?.Value ?? string.Empty;
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
