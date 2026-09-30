namespace eShop.Ordering.API.Application.IntegrationEvents.EventHandling;

public class OrderPaymentSucceededIntegrationEventHandler : IIntegrationEventHandler<OrderPaymentSucceededIntegrationEvent>
{
    private readonly IMediator mediator;
    private readonly ILogger<OrderPaymentSucceededIntegrationEventHandler> logger;

    private OrderPaymentSucceededIntegrationEventHandler(
        IMediator mediator,
        ILogger<OrderPaymentSucceededIntegrationEventHandler> logger)
    {
        this.mediator = mediator;
        this.logger = logger;
    }

    public static string OnPaymentSucceeded(int orderId) =>
        GraftHost.Block(async provider =>
        {
            await Apply(provider, orderId).ConfigureAwait(false);
            return GraftHost.Ok();
        });

    internal static Task Apply(IServiceProvider provider, int orderId) =>
        Create(provider).Handle(new OrderPaymentSucceededIntegrationEvent(orderId));

    public async Task Handle(OrderPaymentSucceededIntegrationEvent @event)
    {
        logger.LogInformation("Handling integration event: {IntegrationEventId} - ({@IntegrationEvent})", @event.Id, @event);

        var command = new SetPaidOrderStatusCommand(@event.OrderId);

        logger.LogInformation(
            "Sending command: {CommandName} - {IdProperty}: {CommandId} ({@Command})",
            command.GetGenericTypeName(),
            nameof(command.OrderNumber),
            command.OrderNumber,
            command);

        await mediator.Send(command);
    }

    private static OrderPaymentSucceededIntegrationEventHandler Create(IServiceProvider provider) =>
        new(
            provider.GetRequiredService<IMediator>(),
            provider.GetRequiredService<ILogger<OrderPaymentSucceededIntegrationEventHandler>>());
}
