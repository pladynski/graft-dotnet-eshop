namespace eShop.Ordering.API.Application.IntegrationEvents.EventHandling;

public class OrderPaymentFailedIntegrationEventHandler : IIntegrationEventHandler<OrderPaymentFailedIntegrationEvent>
{
    private readonly IMediator mediator;
    private readonly ILogger<OrderPaymentFailedIntegrationEventHandler> logger;

    private OrderPaymentFailedIntegrationEventHandler(
        IMediator mediator,
        ILogger<OrderPaymentFailedIntegrationEventHandler> logger)
    {
        this.mediator = mediator;
        this.logger = logger;
    }

    public static GraftStatus OnPaymentFailed(int orderId) =>
        GraftHost.Block(async provider =>
        {
            await Apply(provider, orderId).ConfigureAwait(false);
            return new GraftStatus("ok");
        });

    internal static Task Apply(IServiceProvider provider, int orderId) =>
        ((IIntegrationEventHandler<OrderPaymentFailedIntegrationEvent>)Create(provider))
            .Handle(new OrderPaymentFailedIntegrationEvent(orderId));

    async Task IIntegrationEventHandler<OrderPaymentFailedIntegrationEvent>.Handle(OrderPaymentFailedIntegrationEvent @event)
    {
        logger.LogInformation("Handling integration event: {IntegrationEventId} - ({@IntegrationEvent})", @event.Id, @event);

        var command = new CancelOrderCommand(@event.OrderId);

        logger.LogInformation(
            "Sending command: {CommandName} - {IdProperty}: {CommandId} ({@Command})",
            command.GetGenericTypeName(),
            nameof(command.OrderNumber),
            command.OrderNumber,
            command);

        await mediator.Send(command);
    }

    private static OrderPaymentFailedIntegrationEventHandler Create(IServiceProvider provider) =>
        new(
            provider.GetRequiredService<IMediator>(),
            provider.GetRequiredService<ILogger<OrderPaymentFailedIntegrationEventHandler>>());
}
