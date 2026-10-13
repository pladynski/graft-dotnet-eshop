namespace eShop.Ordering.API.Application.IntegrationEvents.EventHandling;

// Does not implement IIntegrationEventHandler<T>. That contract is a positional record, and gg 1.4.9 fails the NuGet build when a hosted type exposes one.
public class OrderStockRejectedIntegrationEventHandler
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

    public static GraftStatus OnStockRejected(int orderId, int[] productIds) =>
        GraftHost.Block(async provider =>
        {
            await Apply(provider, orderId, productIds ?? []).ConfigureAwait(false);
            return new GraftStatus("ok");
        });

    internal static Task Apply(IServiceProvider provider, int orderId, IEnumerable<int> productIds)
    {
        var items = productIds.Select(id => new ConfirmedOrderStockItem(id, false)).ToList();
        return Create(provider).Handle(new OrderStockRejectedIntegrationEvent(orderId, items));
    }

    private async Task Handle(OrderStockRejectedIntegrationEvent @event)
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
