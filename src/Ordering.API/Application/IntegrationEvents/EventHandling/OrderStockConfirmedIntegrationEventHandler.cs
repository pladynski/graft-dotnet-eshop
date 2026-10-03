namespace eShop.Ordering.API.Application.IntegrationEvents.EventHandling;

public class OrderStockConfirmedIntegrationEventHandler : IIntegrationEventHandler<OrderStockConfirmedIntegrationEvent>
{
    private readonly IMediator mediator;
    private readonly ILogger<OrderStockConfirmedIntegrationEventHandler> logger;

    private OrderStockConfirmedIntegrationEventHandler(
        IMediator mediator,
        ILogger<OrderStockConfirmedIntegrationEventHandler> logger)
    {
        this.mediator = mediator;
        this.logger = logger;
    }

    public static GraftStatus OnStockConfirmed(int orderId) =>
        GraftHost.Block(async provider =>
        {
            await Apply(provider, orderId).ConfigureAwait(false);
            return new GraftStatus("ok");
        });

    internal static Task Apply(IServiceProvider provider, int orderId) =>
        ((IIntegrationEventHandler<OrderStockConfirmedIntegrationEvent>)Create(provider))
            .Handle(new OrderStockConfirmedIntegrationEvent(orderId));

    async Task IIntegrationEventHandler<OrderStockConfirmedIntegrationEvent>.Handle(OrderStockConfirmedIntegrationEvent @event)
    {
        logger.LogInformation("Handling integration event: {IntegrationEventId} - ({@IntegrationEvent})", @event.Id, @event);

        var command = new SetStockConfirmedOrderStatusCommand(@event.OrderId);

        logger.LogInformation(
            "Sending command: {CommandName} - {IdProperty}: {CommandId} ({@Command})",
            command.GetGenericTypeName(),
            nameof(command.OrderNumber),
            command.OrderNumber,
            command);

        await mediator.Send(command);
    }

    private static OrderStockConfirmedIntegrationEventHandler Create(IServiceProvider provider) =>
        new(
            provider.GetRequiredService<IMediator>(),
            provider.GetRequiredService<ILogger<OrderStockConfirmedIntegrationEventHandler>>());
}
