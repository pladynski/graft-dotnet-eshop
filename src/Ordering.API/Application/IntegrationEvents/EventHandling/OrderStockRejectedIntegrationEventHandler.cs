namespace eShop.Ordering.API.Application.IntegrationEvents.EventHandling;

public class OrderStockRejectedIntegrationEventHandler : IIntegrationEventHandler<OrderStockRejectedIntegrationEvent>
{
    private readonly IMediator mediator;
    private readonly ILogger<OrderStockRejectedIntegrationEventHandler> logger;

    private OrderStockRejectedIntegrationEventHandler(
        IMediator mediator,
        ILogger<OrderStockRejectedIntegrationEventHandler> logger)
    {
        this.mediator = mediator;
        this.logger = logger;
    }

    public static string OnStockRejected(int orderId, string productIds) =>
        GraftHost.Block(async provider =>
        {
            await Apply(provider, orderId, GraftHost.ParseIds(productIds)).ConfigureAwait(false);
            return GraftHost.Ok();
        });

    internal static Task Apply(IServiceProvider provider, int orderId, IEnumerable<int> productIds)
    {
        var items = productIds.Select(id => new ConfirmedOrderStockItem(id, false)).ToList();
        return Create(provider).Handle(new OrderStockRejectedIntegrationEvent(orderId, items));
    }

    public async Task Handle(OrderStockRejectedIntegrationEvent @event)
    {
        logger.LogInformation("Handling integration event: {IntegrationEventId} - ({@IntegrationEvent})", @event.Id, @event);

        var orderStockRejectedItems = @event.OrderStockItems
            .FindAll(c => !c.HasStock)
            .Select(c => c.ProductId)
            .ToList();

        var command = new SetStockRejectedOrderStatusCommand(@event.OrderId, orderStockRejectedItems);

        logger.LogInformation(
            "Sending command: {CommandName} - {IdProperty}: {CommandId} ({@Command})",
            command.GetGenericTypeName(),
            nameof(command.OrderNumber),
            command.OrderNumber,
            command);

        await mediator.Send(command);
    }

    private static OrderStockRejectedIntegrationEventHandler Create(IServiceProvider provider) =>
        new(
            provider.GetRequiredService<IMediator>(),
            provider.GetRequiredService<ILogger<OrderStockRejectedIntegrationEventHandler>>());
}
