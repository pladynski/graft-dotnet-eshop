namespace Webhooks.API.IntegrationEvents;

public class OrderStatusChangedToShippedIntegrationEventHandler : IIntegrationEventHandler<OrderStatusChangedToShippedIntegrationEvent>
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
            var handler = (IIntegrationEventHandler<OrderStatusChangedToShippedIntegrationEvent>)Create(provider);
            await handler.Handle(new OrderStatusChangedToShippedIntegrationEvent(orderId, orderStatus ?? string.Empty, buyerName ?? string.Empty)).ConfigureAwait(false);
            return new GraftStatus("ok");
        });

    async Task IIntegrationEventHandler<OrderStatusChangedToShippedIntegrationEvent>.Handle(OrderStatusChangedToShippedIntegrationEvent @event)
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
