namespace Webhooks.API.IntegrationEvents;

// Does not implement IIntegrationEventHandler<T>. That contract is a positional record, and gg 1.4.9 fails the NuGet build when a hosted type exposes one.
public class OrderStatusChangedToShippedIntegrationEventHandler
{
    private readonly IWebhooksRetriever retriever;
    private readonly IWebhooksSender sender;
    private readonly ILogger<OrderStatusChangedToShippedIntegrationEventHandler> logger;

    private OrderStatusChangedToShippedIntegrationEventHandler(
        IWebhooksRetriever retriever,
        IWebhooksSender sender,
        ILogger<OrderStatusChangedToShippedIntegrationEventHandler> logger)
    {
        this.retriever = retriever;
        this.sender = sender;
        this.logger = logger;
    }

    public static GraftStatus OnOrderShipped(int orderId, string orderStatus, string buyerName) =>
        GraftHost.Block(async provider =>
        {
            await Create(provider).Handle(new OrderStatusChangedToShippedIntegrationEvent(orderId, orderStatus ?? string.Empty, buyerName ?? string.Empty)).ConfigureAwait(false);
            return new GraftStatus("ok");
        });

    private async Task Handle(OrderStatusChangedToShippedIntegrationEvent @event)
    {
        var subscriptions = await retriever.GetSubscriptionsOfType(WebhookType.OrderShipped);

        logger.LogInformation("Received OrderStatusChangedToShippedIntegrationEvent and got {SubscriptionCount} subscriptions to process", subscriptions.Count());

        var whook = new WebhookData(WebhookType.OrderShipped, @event);

        await sender.SendAll(subscriptions, whook);
    }

    private static OrderStatusChangedToShippedIntegrationEventHandler Create(IServiceProvider provider) =>
        new(
            provider.GetRequiredService<IWebhooksRetriever>(),
            provider.GetRequiredService<IWebhooksSender>(),
            provider.GetRequiredService<ILogger<OrderStatusChangedToShippedIntegrationEventHandler>>());
}
