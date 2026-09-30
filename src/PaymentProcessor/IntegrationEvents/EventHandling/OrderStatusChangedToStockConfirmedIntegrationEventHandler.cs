namespace eShop.PaymentProcessor.IntegrationEvents.EventHandling;

public class OrderStatusChangedToStockConfirmedIntegrationEventHandler
{
    private readonly IOptionsMonitor<PaymentOptions> options;
    private readonly ILogger<OrderStatusChangedToStockConfirmedIntegrationEventHandler> logger;
    private static readonly Lazy<Task<IHost>> HostTask = new(StartHostAsync);

    internal OrderStatusChangedToStockConfirmedIntegrationEventHandler(
        IOptionsMonitor<PaymentOptions> options,
        ILogger<OrderStatusChangedToStockConfirmedIntegrationEventHandler> logger)
    {
        this.options = options;
        this.logger = logger;
    }

    public static string OnStockConfirmed(int orderId) =>
        Task.Run(async () =>
        {
            var host = await HostTask.Value.ConfigureAwait(false);
            await using var scope = host.Services.CreateAsyncScope();
            var provider = scope.ServiceProvider;
            var handler = new OrderStatusChangedToStockConfirmedIntegrationEventHandler(
                provider.GetRequiredService<IOptionsMonitor<PaymentOptions>>(),
                provider.GetRequiredService<ILogger<OrderStatusChangedToStockConfirmedIntegrationEventHandler>>());
            return await handler.Handle(new OrderStatusChangedToStockConfirmedIntegrationEvent(orderId)).ConfigureAwait(false);
        }).GetAwaiter().GetResult();

    public Task<string> Handle(OrderStatusChangedToStockConfirmedIntegrationEvent @event)
    {
        logger.LogInformation("Handling integration event: {IntegrationEventId} - ({@IntegrationEvent})", @event.Id, @event);

        // Business feature comment:
        // When OrderStatusChangedToStockConfirmed Integration Event is handled.
        // Here we're simulating that we'd be performing the payment against any payment gateway
        // Instead of a real payment we just take the env. var to simulate the payment
        // The payment can be successful or it can fail

        var outcome = options.CurrentValue.PaymentSucceeded ? "succeeded" : "failed";

        logger.LogInformation("Payment outcome {Outcome} for order {OrderId}", outcome, @event.OrderId);

        return Task.FromResult(outcome);
    }

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
        if (string.IsNullOrEmpty(baseDir))
        {
            baseDir = Path.GetDirectoryName(typeof(OrderStatusChangedToStockConfirmedIntegrationEventHandler).Assembly.Location) ?? string.Empty;
        }

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
