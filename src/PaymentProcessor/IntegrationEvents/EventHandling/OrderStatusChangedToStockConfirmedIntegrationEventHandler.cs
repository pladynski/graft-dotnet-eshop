namespace eShop.PaymentProcessor.IntegrationEvents.EventHandling;

public class OrderStatusChangedToStockConfirmedIntegrationEventHandler(
    IOptionsMonitor<PaymentOptions> options,
    ILogger<OrderStatusChangedToStockConfirmedIntegrationEventHandler> logger)
{
    public Task<string> Handle(OrderStatusChangedToStockConfirmedIntegrationEvent @event)
    {
        logger.LogInformation("Handling integration event: {IntegrationEventId} - ({@IntegrationEvent})", @event.Id, @event);

        // Business feature comment:
        // When OrderStatusChangedToStockConfirmed Integration Event is handled.
        // Here we're simulating that we'd be performing the payment against any payment gateway
        // Instead of a real payment we just take the env. var to simulate the payment
        // The payment can be successful or it can fail

        var outcome = options.CurrentValue.PaymentSucceeded ? "succeeded" : "failed";

        logger.LogInformation("Payment outcome {Outcome} for order {OrderId}", outcome, @event.OrderId);

        return Task.FromResult(outcome);
    }
}
