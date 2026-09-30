namespace Webhooks.API.IntegrationEvents;

public class OrderStatusChangedToPaidIntegrationEventHandler : IIntegrationEventHandler<OrderStatusChangedToPaidIntegrationEvent>
{
    private readonly IWebhooksRetriever retriever;
    private readonly IWebhooksSender sender;
    private readonly ILogger<OrderStatusChangedToShippedIntegrationEventHandler> logger;

    private OrderStatusChangedToPaidIntegrationEventHandler(
        IWebhooksRetriever retriever,
        IWebhooksSender sender,
        ILogger<OrderStatusChangedToShippedIntegrationEventHandler> logger)
    {
        this.retriever = retriever;
        this.sender = sender;
        this.logger = logger;
    }

    public static string OnOrderPaid(int orderId, string stockItemsJson) =>
        GraftHost.Block(async provider =>
        {
            var handler = Create(provider);
            await handler.Handle(new OrderStatusChangedToPaidIntegrationEvent(orderId, GraftHost.ReadStock(stockItemsJson))).ConfigureAwait(false);
            return GraftHost.Ok();
        });

    public async Task Handle(OrderStatusChangedToPaidIntegrationEvent @event)
    {
        var subscriptions = await retriever.GetSubscriptionsOfType(WebhookType.OrderPaid);

        logger.LogInformation("Received OrderStatusChangedToShippedIntegrationEvent and got {SubscriptionsCount} subscriptions to process", subscriptions.Count());

        var whook = new WebhookData(WebhookType.OrderPaid, @event);

        await sender.SendAll(subscriptions, whook);
    }

    private static OrderStatusChangedToPaidIntegrationEventHandler Create(IServiceProvider provider) =>
        new(
            provider.GetRequiredService<IWebhooksRetriever>(),
            provider.GetRequiredService<IWebhooksSender>(),
            provider.GetRequiredService<ILogger<OrderStatusChangedToShippedIntegrationEventHandler>>());
}
