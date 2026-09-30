// Generated webhooks graft. Swap ESHOP_GRAFT_PLUGIN_NAME to move this call to another broker plugin.
using System.Globalization;
using eShop.Graft;
using WebhookGraft = graft.nuget.Webhooks.API.IntegrationEvents.ProductPriceChangedIntegrationEventHandler;

namespace eShop.Catalog.API.IntegrationEvents;

internal static class PriceChangedGraft
{
    private static readonly object Gate = new();
    private static bool ready;

    public static void Publish(ProductPriceChangedIntegrationEvent price)
    {
        if (!GraftRabbit.TransportEnabled)
        {
            return;
        }

        Ensure();
        WebhookGraft.OnProductPriceChanged(
            price.ProductId,
            price.NewPrice.ToString(CultureInfo.InvariantCulture),
            price.OldPrice.ToString(CultureInfo.InvariantCulture));
    }

    private static void Ensure()
    {
        if (ready)
        {
            return;
        }

        lock (Gate)
        {
            if (ready)
            {
                return;
            }

            graft.nuget.Webhooks.API.GraftConfig.SetConfig(GraftRabbit.ClientConfig("graft.nuget.Webhooks.API", "eshop.webhooks"));
            ready = true;
        }
    }
}
