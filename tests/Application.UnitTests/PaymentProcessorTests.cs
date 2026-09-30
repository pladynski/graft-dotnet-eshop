using eShop.PaymentProcessor;
using eShop.PaymentProcessor.IntegrationEvents.EventHandling;
using eShop.PaymentProcessor.IntegrationEvents.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace eShop.Application.UnitTests;

[TestClass]
public class PaymentProcessorTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ReturnsConfiguredPaymentOutcome(bool paymentSucceeded)
    {
        var options = Substitute.For<IOptionsMonitor<PaymentOptions>>();
        options.CurrentValue.Returns(new PaymentOptions { PaymentSucceeded = paymentSucceeded });
        var handler = new OrderStatusChangedToStockConfirmedIntegrationEventHandler(
            options,
            NullLogger<OrderStatusChangedToStockConfirmedIntegrationEventHandler>.Instance);

        var outcome = await handler.Handle(new OrderStatusChangedToStockConfirmedIntegrationEvent(42));

        Assert.AreEqual(paymentSucceeded ? "succeeded" : "failed", outcome);
    }
}
