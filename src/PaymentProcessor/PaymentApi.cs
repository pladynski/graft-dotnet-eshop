// Graftcode payment slice — the stock-confirmed handler is the public method Gateway hosts.
using Microsoft.Extensions.DependencyInjection;

namespace eShop.PaymentProcessor;

public static class PaymentApi
{
    private static readonly Lazy<Task<IHost>> HostTask = new(StartHostAsync);

    public static int HostPid() => Environment.ProcessId;

    public static string OnStockConfirmed(int orderId) =>
        Task.Run(async () =>
        {
            var host = await HostTask.Value.ConfigureAwait(false);
            await using var scope = host.Services.CreateAsyncScope();
            var handler = ActivatorUtilities.CreateInstance<OrderStatusChangedToStockConfirmedIntegrationEventHandler>(scope.ServiceProvider);
            return await handler.Handle(new OrderStatusChangedToStockConfirmedIntegrationEvent(orderId)).ConfigureAwait(false);
        }).GetAwaiter().GetResult();

    private static async Task<IHost> StartHostAsync()
    {
        var contentRoot = ResolveContentRoot();
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = contentRoot,
            ApplicationName = "PaymentProcessor"
        });

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string>
        {
            ["EshopGraftHost"] = "true"
        });
        builder.AddBasicServiceDefaults();
        builder.Services.AddOptions<PaymentOptions>()
            .BindConfiguration(nameof(PaymentOptions));

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
            if (File.Exists(Path.Combine(dir.FullName, "PaymentProcessor.csproj"))
                && File.Exists(Path.Combine(dir.FullName, "appsettings.json")))
            {
                return dir.FullName;
            }
        }

        return baseDir;
    }
}
