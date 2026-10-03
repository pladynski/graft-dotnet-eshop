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

    public static GraftStatus OnOrderPaid(int orderId, StockRequest stockItems) =>
        GraftHost.Block(async provider =>
        {
            var handler = (IIntegrationEventHandler<OrderStatusChangedToPaidIntegrationEvent>)Create(provider);
            await handler.Handle(new OrderStatusChangedToPaidIntegrationEvent(orderId, ReadStock(stockItems))).ConfigureAwait(false);
            return new GraftStatus("ok");
        });

    async Task IIntegrationEventHandler<OrderStatusChangedToPaidIntegrationEvent>.Handle(OrderStatusChangedToPaidIntegrationEvent @event)
    {
        var subscriptions = await retriever.GetSubscriptionsOfType(WebhookType.OrderPaid);

        logger.LogInformation("Received OrderStatusChangedToShippedIntegrationEvent and got {SubscriptionsCount} subscriptions to process", subscriptions.Count());

        var whook = new WebhookData(WebhookType.OrderPaid, @event);

        await sender.SendAll(subscriptions, whook);
    }

    private static List<OrderStockItem> ReadStock(StockRequest stockItems)
    {
        var productIds = stockItems?.ProductIds ?? [];
        var units = stockItems?.Units ?? [];
        var count = Math.Min(productIds.Length, units.Length);
        var items = new List<OrderStockItem>(count);
        for (var i = 0; i < count; i++)
        {
            items.Add(new OrderStockItem(productIds[i], units[i]));
        }

        return items;
    }

    private static OrderStatusChangedToPaidIntegrationEventHandler Create(IServiceProvider provider) =>
        new(
            provider.GetRequiredService<IWebhooksRetriever>(),
            provider.GetRequiredService<IWebhooksSender>(),
            provider.GetRequiredService<ILogger<OrderStatusChangedToShippedIntegrationEventHandler>>());
}
