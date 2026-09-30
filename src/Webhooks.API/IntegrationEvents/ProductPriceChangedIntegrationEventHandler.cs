namespace Webhooks.API.IntegrationEvents;

public class ProductPriceChangedIntegrationEventHandler : IIntegrationEventHandler<ProductPriceChangedIntegrationEvent>
{
    private ProductPriceChangedIntegrationEventHandler()
    {
    }

    public static GraftStatus OnProductPriceChanged(int productId, double newPrice, double oldPrice) =>
        GraftHost.Block(async _ =>
        {
            var handler = (IIntegrationEventHandler<ProductPriceChangedIntegrationEvent>)new ProductPriceChangedIntegrationEventHandler();
            await handler.Handle(new ProductPriceChangedIntegrationEvent(productId, (decimal)newPrice, (decimal)oldPrice)).ConfigureAwait(false);
            return new GraftStatus("ok");
        });

    Task IIntegrationEventHandler<ProductPriceChangedIntegrationEvent>.Handle(ProductPriceChangedIntegrationEvent @event)
    {
        return Task.CompletedTask;
    }
}
