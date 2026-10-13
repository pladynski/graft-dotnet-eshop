namespace Webhooks.API.IntegrationEvents;

// Does not implement IIntegrationEventHandler<T>. That contract is a positional record, and gg 1.4.9 fails the NuGet build when a hosted type exposes one.
public class ProductPriceChangedIntegrationEventHandler
{
    private ProductPriceChangedIntegrationEventHandler()
    {
    }

    public static GraftStatus OnProductPriceChanged(int productId, double newPrice, double oldPrice) =>
        GraftHost.Block(async _ =>
        {
            await new ProductPriceChangedIntegrationEventHandler().Handle(new ProductPriceChangedIntegrationEvent(productId, (decimal)newPrice, (decimal)oldPrice)).ConfigureAwait(false);
            return new GraftStatus("ok");
        });

    private Task Handle(ProductPriceChangedIntegrationEvent @event)
    {
        return Task.CompletedTask;
    }
}
