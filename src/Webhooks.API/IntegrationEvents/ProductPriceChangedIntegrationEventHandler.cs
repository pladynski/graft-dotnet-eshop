using System.Globalization;

namespace Webhooks.API.IntegrationEvents;

public class ProductPriceChangedIntegrationEventHandler : IIntegrationEventHandler<ProductPriceChangedIntegrationEvent>
{
    private ProductPriceChangedIntegrationEventHandler()
    {
    }

    public static string OnProductPriceChanged(int productId, string newPrice, string oldPrice) =>
        GraftHost.Block(async _ =>
        {
            var handler = new ProductPriceChangedIntegrationEventHandler();
            var integrationEvent = new ProductPriceChangedIntegrationEvent(
                productId,
                decimal.Parse(newPrice, CultureInfo.InvariantCulture),
                decimal.Parse(oldPrice, CultureInfo.InvariantCulture));
            await handler.Handle(integrationEvent).ConfigureAwait(false);
            return GraftHost.Ok();
        });

    public Task Handle(ProductPriceChangedIntegrationEvent @event)
    {
        return Task.CompletedTask;
    }
}
