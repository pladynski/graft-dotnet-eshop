// Graftcode ordering slice — integration handlers are the public methods Gateway hosts.
using Microsoft.Extensions.DependencyInjection;

namespace eShop.Ordering.API;

public static class OrderingApi
{
    private static readonly Lazy<Task<IHost>> HostTask = new(StartHostAsync);
    private static IServiceProvider AttachedServices;

    internal static void Attach(IServiceProvider services) => AttachedServices = services;

    public static int HostPid() => Environment.ProcessId;

    public static string OnGracePeriodConfirmed(int orderId) =>
        Block(async provider =>
        {
            await ApplyGracePeriodAsync(provider, orderId);
            return Ok();
        });

    public static string OnStockConfirmed(int orderId) =>
        Block(async provider =>
        {
            await ApplyStockConfirmedAsync(provider, orderId);
            return Ok();
        });

    public static string OnStockRejected(int orderId, string productIds) =>
        Block(async provider =>
        {
            await ApplyStockRejectedAsync(provider, orderId, ParseIds(productIds));
            return Ok();
        });

    public static string OnPaymentSucceeded(int orderId) =>
        Block(async provider =>
        {
            await ApplyPaymentSucceededAsync(provider, orderId);
            return Ok();
        });

    public static string OnPaymentFailed(int orderId) =>
        Block(async provider =>
        {
            await ApplyPaymentFailedAsync(provider, orderId);
            return Ok();
        });

    internal static Task ApplyGracePeriodAsync(IServiceProvider provider, int orderId)
    {
        var handler = ActivatorUtilities.CreateInstance<GracePeriodConfirmedIntegrationEventHandler>(provider);
        return handler.Handle(new GracePeriodConfirmedIntegrationEvent(orderId));
    }

    internal static Task ApplyStockConfirmedAsync(IServiceProvider provider, int orderId)
    {
        var handler = ActivatorUtilities.CreateInstance<OrderStockConfirmedIntegrationEventHandler>(provider);
        return handler.Handle(new OrderStockConfirmedIntegrationEvent(orderId));
    }

    internal static Task ApplyStockRejectedAsync(IServiceProvider provider, int orderId, IReadOnlyList<int> productIds)
    {
        var items = productIds.Select(id => new ConfirmedOrderStockItem(id, false)).ToList();
        var handler = ActivatorUtilities.CreateInstance<OrderStockRejectedIntegrationEventHandler>(provider);
        return handler.Handle(new OrderStockRejectedIntegrationEvent(orderId, items));
    }

    internal static Task ApplyPaymentSucceededAsync(IServiceProvider provider, int orderId)
    {
        var handler = ActivatorUtilities.CreateInstance<OrderPaymentSucceededIntegrationEventHandler>(provider);
        return handler.Handle(new OrderPaymentSucceededIntegrationEvent(orderId));
    }

    internal static Task ApplyPaymentFailedAsync(IServiceProvider provider, int orderId)
    {
        var handler = ActivatorUtilities.CreateInstance<OrderPaymentFailedIntegrationEventHandler>(provider);
        return handler.Handle(new OrderPaymentFailedIntegrationEvent(orderId));
    }

    private static string Block(Func<IServiceProvider, Task<string>> action) =>
        Task.Run(async () =>
        {
            var provider = AttachedServices;
            if (provider is null)
            {
                var host = await HostTask.Value.ConfigureAwait(false);
                provider = host.Services;
            }

            await using var scope = provider.CreateAsyncScope();
            return await action(scope.ServiceProvider).ConfigureAwait(false);
        }).GetAwaiter().GetResult();

    private static async Task<IHost> StartHostAsync()
    {
        var contentRoot = ResolveContentRoot();
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = contentRoot,
            ApplicationName = "Ordering.API"
        });

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string>
        {
            ["EshopGraftHost"] = "true"
        });
        builder.AddServiceDefaults();
        builder.AddApplicationServices();

        if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("orderingdb")))
        {
            throw new InvalidOperationException(
                "OrderingApi requires ConnectionStrings__orderingdb. Aspire injects this for ordering-api; a standalone Gateway process needs it set.");
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
            if (File.Exists(Path.Combine(dir.FullName, "Ordering.API.csproj"))
                && File.Exists(Path.Combine(dir.FullName, "appsettings.json")))
            {
                return dir.FullName;
            }
        }

        return baseDir;
    }

    private static List<int> ParseIds(string productIds)
    {
        var ids = new List<int>();
        if (string.IsNullOrWhiteSpace(productIds))
        {
            return ids;
        }

        foreach (var part in productIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, out var id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    private static string Ok() => "{\"status\":\"ok\"}";
}
