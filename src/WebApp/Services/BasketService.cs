// Graftcode basket slice — same BasketService, calls the generated Basket graft.
using System.Text.Json;
using graft.nuget.Basket.API;
using Microsoft.AspNetCore.Components.Authorization;
using BasketApi = graft.nuget.eShop.Basket.API.BasketService;

namespace eShop.WebApp.Services;

public class BasketService(AuthenticationStateProvider authenticationStateProvider)
{
    public const string DefaultHost = "ws://localhost:8000/ws";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
        var lines = JsonSerializer.Deserialize<List<BasketLine>>(BasketApi.GetBasket(await BuyerIdAsync()), JsonOptions) ?? [];
        return lines.Select(line => new BasketQuantity(line.ProductId, line.Quantity)).ToList();
    }

    public async Task DeleteBasketAsync()
    {
        ThrowIfUnauthenticated(BasketApi.DeleteBasket(await BuyerIdAsync()));
    }

    public async Task UpdateBasketAsync(IReadOnlyCollection<BasketQuantity> basket)
    {
        var payload = JsonSerializer.Serialize(
            basket.Select(item => new BasketLine(item.ProductId, item.Quantity)),
            JsonOptions);
        ThrowIfUnauthenticated(BasketApi.UpdateBasket(await BuyerIdAsync(), payload));
    }

    private static void ThrowIfUnauthenticated(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json[0] != '{')
        {
            return;
        }

        var status = JsonSerializer.Deserialize<StatusPayload>(json, JsonOptions);
        if (string.Equals(status?.Status, "unauthenticated", StringComparison.Ordinal))
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

    private sealed record BasketLine(int ProductId, int Quantity);

    private sealed record StatusPayload(string Status);
}

public record BasketQuantity(int ProductId, int Quantity);
