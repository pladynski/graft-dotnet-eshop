using Microsoft.Extensions.Options;

namespace eShop.OrderProcessor.Services
{
    public class GracePeriodManagerService(
        IOptions<BackgroundTaskOptions> options,
        ILogger<GracePeriodManagerService> logger,
        IGracePeriodOrdersRepository repository) : BackgroundService
    {
        private readonly BackgroundTaskOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var delayTime = TimeSpan.FromSeconds(_options.CheckUpdateTime);

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("GracePeriodManagerService is starting.");
                stoppingToken.Register(() => logger.LogDebug("GracePeriodManagerService background task is stopping."));
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogDebug("GracePeriodManagerService background task is doing background work.");
                }

                await CheckConfirmedGracePeriodOrders(stoppingToken);

                await Task.Delay(delayTime, stoppingToken);
            }

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("GracePeriodManagerService background task is stopping.");
            }
        }

        internal async Task CheckConfirmedGracePeriodOrders(CancellationToken cancellationToken = default)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("Checking confirmed grace period orders");
            }

            var orderIds = await repository.GetConfirmedGracePeriodOrdersAsync(
                TimeSpan.FromMinutes(_options.GracePeriodTime),
                cancellationToken);

            foreach (var orderId in orderIds)
            {
                logger.LogInformation("Confirming grace period for order {OrderId}", orderId);

                OrderingLifecycle.GracePeriodConfirmed(orderId);
            }
        }

    }
}
