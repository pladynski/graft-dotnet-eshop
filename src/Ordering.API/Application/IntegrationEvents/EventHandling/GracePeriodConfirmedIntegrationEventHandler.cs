namespace eShop.Ordering.API.Application.IntegrationEvents.EventHandling;

// Does not implement IIntegrationEventHandler<T>. That contract is a positional record, and gg 1.4.9 fails the NuGet build when a hosted type exposes one.
public class GracePeriodConfirmedIntegrationEventHandler
{
    private readonly IMediator mediator;
    private readonly ILogger<GracePeriodConfirmedIntegrationEventHandler> logger;

    // Private so the generated graft only sees the public static method.
    private GracePeriodConfirmedIntegrationEventHandler(
        IMediator mediator,
        ILogger<GracePeriodConfirmedIntegrationEventHandler> logger)
    {
        this.mediator = mediator;
        this.logger = logger;
    }

    public static GraftStatus OnGracePeriodConfirmed(int orderId) =>
        GraftHost.Block(async provider =>
        {
            await Apply(provider, orderId).ConfigureAwait(false);
            return new GraftStatus("ok");
        });

    internal static Task Apply(IServiceProvider provider, int orderId) =>
        Create(provider).Handle(new GracePeriodConfirmedIntegrationEvent(orderId));

    /// <summary>
    /// Event handler which confirms that the grace period
    /// has been completed and order will not initially be cancelled.
    /// Therefore, the order process continues for validation. 
    /// </summary>
    /// <param name="event">       
    /// </param>
    /// <returns></returns>
    private async Task Handle(GracePeriodConfirmedIntegrationEvent @event)
    {
        logger.LogInformation("Handling integration event: {IntegrationEventId} - ({@IntegrationEvent})", @event.Id, @event);

        var command = new SetAwaitingValidationOrderStatusCommand(@event.OrderId);

        logger.LogInformation(
            "Sending command: {CommandName} - {IdProperty}: {CommandId} ({@Command})",
            command.GetGenericTypeName(),
            nameof(command.OrderNumber),
            command.OrderNumber,
            command);

        await mediator.Send(command);
    }

    private static GracePeriodConfirmedIntegrationEventHandler Create(IServiceProvider provider) =>
        new(
            provider.GetRequiredService<IMediator>(),
            provider.GetRequiredService<ILogger<GracePeriodConfirmedIntegrationEventHandler>>());
}
