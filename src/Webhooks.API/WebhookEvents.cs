// Graftcode webhooks slice — paid, shipped, and price handlers are the public methods Gateway hosts.
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;

namespace Webhooks.API;

public static class WebhookEvents
{
    private static readonly Lazy<Task<IHost>> HostTask = new(StartHostAsync);

    public static int HostPid() => Environment.ProcessId;

    public static string OnOrderPaid(int orderId, string stockItemsJson) =>
        Block(async provider =>
        {
            var items = ReadStock(stockItemsJson);
            var handler = ActivatorUtilities.CreateInstance<OrderStatusChangedToPaidIntegrationEventHandler>(provider);
            await handler.Handle(new OrderStatusChangedToPaidIntegrationEvent(orderId, items)).ConfigureAwait(false);
            return Ok();
        });

    public static string OnOrderShipped(int orderId, string orderStatus, string buyerName) =>
        Block(async provider =>
        {
            var handler = ActivatorUtilities.CreateInstance<OrderStatusChangedToShippedIntegrationEventHandler>(provider);
            await handler.Handle(new OrderStatusChangedToShippedIntegrationEvent(orderId, orderStatus ?? string.Empty, buyerName ?? string.Empty)).ConfigureAwait(false);
            return Ok();
        });

    public static string OnProductPriceChanged(int productId, string newPrice, string oldPrice) =>
        Block(async provider =>
        {
            var handler = ActivatorUtilities.CreateInstance<ProductPriceChangedIntegrationEventHandler>(provider);
            var integrationEvent = new ProductPriceChangedIntegrationEvent(
                productId,
                decimal.Parse(newPrice, CultureInfo.InvariantCulture),
                decimal.Parse(oldPrice, CultureInfo.InvariantCulture));
            await handler.Handle(integrationEvent).ConfigureAwait(false);
            return Ok();
        });

    private static string Block(Func<IServiceProvider, Task<string>> action) =>
        Task.Run(async () =>
        {
            var host = await HostTask.Value.ConfigureAwait(false);
            await using var scope = host.Services.CreateAsyncScope();
            return await action(scope.ServiceProvider).ConfigureAwait(false);
        }).GetAwaiter().GetResult();

    private static async Task<IHost> StartHostAsync()
    {
        var contentRoot = ResolveContentRoot();
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = contentRoot,
            ApplicationName = "Webhooks.API"
        });

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string>
        {
            ["EshopGraftHost"] = "true"
        });
        builder.AddServiceDefaults();
        builder.AddApplicationServices();

        if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("webhooksdb")))
        {
            throw new InvalidOperationException(
                "WebhookEvents requires ConnectionStrings__webhooksdb. Aspire injects this for webhooks-api; a standalone Gateway process needs it set.");
        }

        var host = builder.Build();
        await host.StartAsync().ConfigureAwait(false);
        return host;
    }

    private static string ResolveContentRoot()
    {
        var baseDir = AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(baseDir, "appsettings.json")))
        {
            return baseDir;
        }

        var dir = new DirectoryInfo(baseDir);
        for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Webhooks.API.csproj"))
                && File.Exists(Path.Combine(dir.FullName, "appsettings.json")))
            {
                return dir.FullName;
            }
        }

        return baseDir;
    }

    private static List<OrderStockItem> ReadStock(string stockItemsJson)
    {
        if (string.IsNullOrWhiteSpace(stockItemsJson))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<OrderStockItem>>(stockItemsJson, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
    }

    private static string Ok() => "{\"status\":\"ok\"}";
}
