// Graftcode ordering slice — same OrderingService, calls the generated Orders graft.
// The access token is the one OpenIdConnect saved (SaveTokens), the same token AddAuthToken sends.
// InvokeWithHeaders binds it to this call. It is not stored in GraftConfig.SetHeaders.
using System.Globalization;
using graft.nuget.Ordering.API;
using Microsoft.AspNetCore.Authentication;
using OrderHit = graft.nuget.eShop.Ordering.API.OrderHit;
using OrderList = graft.nuget.eShop.Ordering.API.OrderList;
using OrderMutation = graft.nuget.eShop.Ordering.API.OrderMutation;
using OrdersApi = graft.nuget.eShop.Ordering.API.OrdersApi;

namespace eShop.WebApp.Services;

public class OrderingService(IHttpContextAccessor httpContextAccessor)
{
    public const string DefaultHost = "ws://localhost:8000/ws";

    static OrderingService() => Configure();

    public static void Configure()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("ORDERING_GRAFT_TRANSPORT"), "rabbitmq", StringComparison.OrdinalIgnoreCase))
        {
            GraftConfig.SetConfig(ReadPluginConfig());
            return;
        }

        var socket = Environment.GetEnvironmentVariable("ORDERING_GRAFT_HOST");
        GraftConfig.Host = string.IsNullOrWhiteSpace(socket) ? DefaultHost : socket.Trim();
        GraftConfig.Stateless = true;
    }

    public async Task<OrderRecord[]> GetOrders()
    {
        var page = await InvokeAsync(() => OrdersApi.ListOrders());
        ThrowIfRejected(page?.Status, page?.Detail);
        return Lines(page);
    }

    public async Task CreateOrder(
        string userName,
        string city,
        string street,
        string state,
        string country,
        string zipCode,
        string cardNumber,
        string cardHolderName,
        DateTime cardExpiration,
        string cardSecurityNumber,
        int cardTypeId,
        IReadOnlyCollection<BasketItem> items,
        Guid requestId)
    {
        var lines = items ?? [];
        var result = await InvokeAsync(() => OrdersApi.CreateOrder(
            requestId.ToString("D"),
            userName ?? string.Empty,
            city ?? string.Empty,
            street ?? string.Empty,
            state ?? string.Empty,
            country ?? string.Empty,
            zipCode ?? string.Empty,
            cardNumber ?? string.Empty,
            cardHolderName ?? string.Empty,
            cardExpiration.ToString("o", CultureInfo.InvariantCulture),
            cardSecurityNumber ?? string.Empty,
            cardTypeId,
            lines.Select(item => item.ProductId).ToArray(),
            lines.Select(item => item.ProductName ?? string.Empty).ToArray(),
            lines.Select(item => item.Quantity).ToArray(),
            lines.Select(item => (double)item.UnitPrice).ToArray()));
        ThrowIfRejected(result?.Status, result?.Detail);
        if (!string.Equals(result?.Status, "created", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(result?.Detail ?? "Create order failed.");
        }
    }

    private async Task<T> InvokeAsync<T>(Func<T> call)
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

    private static OrderRecord[] Lines(OrderList? page)
    {
        // gg 1.4.9: an empty int[] property throws RuntimeBinderException
        // (Cannot convert type 'object[]' to 'int[]'). Do not read the arrays when Count is 0.
        if (page is null || page.Count <= 0)
        {
            return [];
        }

        var numbers = page.OrderNumbers;
        var dates = page.Dates;
        var statuses = page.Statuses;
        var totals = page.Totals;
        if (numbers is null || dates is null || statuses is null || totals is null)
        {
            return [];
        }

        var count = Math.Min(page.Count, Math.Min(Math.Min(numbers.Length, dates.Length), Math.Min(statuses.Length, totals.Length)));
        var orders = new OrderRecord[count];
        for (var i = 0; i < count; i++)
        {
            var date = DateTime.Parse(dates[i], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            orders[i] = new OrderRecord(numbers[i], date, statuses[i], (decimal)totals[i]);
        }

        return orders;
    }

    private static void ThrowIfRejected(string? status, string? detail)
    {
        if (string.Equals(status, "unauthenticated", StringComparison.Ordinal)
            || string.Equals(status, "forbidden", StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(string.IsNullOrEmpty(detail) ? "You must be logged in." : detail);
        }
    }

    private static string ReadPluginConfig()
    {
        var path = Environment.GetEnvironmentVariable("ORDERING_GRAFT_PLUGIN_CONFIG");
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            return File.ReadAllText(path);
        }

        var pluginHost = Environment.GetEnvironmentVariable("ORDERING_GRAFT_PLUGIN_HOST");
        if (string.IsNullOrWhiteSpace(pluginHost))
        {
            pluginHost = "localhost:5672";
        }

        return $$"""
        {
          "configurations": {
            "graft.nuget.Ordering.API": {
              "runtime": "netcore",
              "host": "{{pluginHost.Trim()}}",
              "stateless": true,
              "plugin": {
                "name": "RabbitmqPlugin",
                "queue": "eshop.ordering",
                "replyQueue": "eshop.ordering.reply",
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

public record OrderRecord(
    int OrderNumber,
    DateTime Date,
    string Status,
    decimal Total);
