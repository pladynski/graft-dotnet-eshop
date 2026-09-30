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

    public static string OnStockConfirmed(int orderId) =>
        GraftHost.Block(async provider =>
        {
            await Apply(provider, orderId).ConfigureAwait(false);
            return GraftHost.Ok();
        });

    internal static Task Apply(IServiceProvider provider, int orderId) =>
        Create(provider).Handle(new OrderStockConfirmedIntegrationEvent(orderId));

    public async Task Handle(OrderStockConfirmedIntegrationEvent @event)
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
