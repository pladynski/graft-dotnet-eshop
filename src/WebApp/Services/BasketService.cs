// Graftcode basket slice — same BasketService, calls BasketService on the gateway instead of gRPC.
using System.Text.Json;
using Hypertube.Netcore.Sdk;
using Hypertube.Netcore.Utils.ConnectionData;
using Microsoft.AspNetCore.Components.Authorization;

namespace eShop.WebApp.Services;

public class BasketService(AuthenticationStateProvider authenticationStateProvider)
{
    public const string DefaultHost = "ws://localhost:8000/ws";
    private const string FacadeType = "eShop.Basket.API.BasketService";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string DefaultPluginConfig =
        """
        {
          "name": "RabbitmqPlugin",
          "host": "localhost",
          "port": 5672,
          "queue": "eshop.basket",
          "replyQueue": "eshop.basket.reply",
          "user": "guest",
          "password": "guest",
          "vhost": "/",
          "rpcTimeoutMs": 30000
        }
        """;

    private readonly object _gate = new();
    private InvocationContext? _facade;

    public async Task<IReadOnlyCollection<BasketQuantity>> GetBasketAsync()
    {
        var json = Call("GetBasket", await BuyerIdAsync());
        var lines = JsonSerializer.Deserialize<List<BasketLine>>(json, JsonOptions) ?? [];
        return lines.Select(line => new BasketQuantity(line.ProductId, line.Quantity)).ToList();
    }

    public async Task DeleteBasketAsync()
    {
        ThrowIfUnauthenticated(Call("DeleteBasket", await BuyerIdAsync()));
    }

    public async Task UpdateBasketAsync(IReadOnlyCollection<BasketQuantity> basket)
    {
        var payload = JsonSerializer.Serialize(
            basket.Select(item => new BasketLine(item.ProductId, item.Quantity)),
            JsonOptions);
        ThrowIfUnauthenticated(Call("UpdateBasket", await BuyerIdAsync(), payload));
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

    private string Call(string method, params object[] args)
    {
        var value = Facade().InvokeStaticMethod(method, args).Execute().GetValue();
        if (value is null)
        {
            return "null";
        }

        return value as string ?? Convert.ToString(value) ?? "null";
    }

    private InvocationContext Facade()
    {
        if (_facade is not null)
        {
            return _facade;
        }

        lock (_gate)
        {
            _facade ??= Open();
        }

        return _facade;
    }

    private static InvocationContext Open()
    {
        var transport = Environment.GetEnvironmentVariable("BASKET_GRAFT_TRANSPORT");
        if (string.Equals(transport, "rabbitmq", StringComparison.OrdinalIgnoreCase))
        {
            var pluginHost = Environment.GetEnvironmentVariable("BASKET_GRAFT_PLUGIN_HOST");
            if (string.IsNullOrWhiteSpace(pluginHost))
            {
                pluginHost = "localhost:5672";
            }

            return RuntimeBridge.Plugin(new PluginConnectionData(pluginHost, ReadPluginConfig()))
                .Netcore()
                .GetType(FacadeType)
                .Execute();
        }

        var socket = Environment.GetEnvironmentVariable("BASKET_GRAFT_HOST");
        if (string.IsNullOrWhiteSpace(socket))
        {
            socket = DefaultHost;
        }

        return RuntimeBridge.WebSocket(new WsConnectionData(socket.Trim()))
            .Netcore()
            .GetType(FacadeType)
            .Execute();
    }

    private static string ReadPluginConfig()
    {
        var path = Environment.GetEnvironmentVariable("BASKET_GRAFT_PLUGIN_CONFIG");
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            return File.ReadAllText(path);
        }

        return DefaultPluginConfig;
    }

    private sealed record BasketLine(int ProductId, int Quantity);

    private sealed record StatusPayload(string Status);
}

public record BasketQuantity(int ProductId, int Quantity);
