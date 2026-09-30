// Generated grafts. RabbitmqPlugin today; another plugin is a config change, not a new call site.
using eShop.Graft;
using BasketGraft = graft.nuget.eShop.Basket.API.BasketService;
using CatalogGraft = graft.nuget.eShop.Catalog.API.CatalogApi;
using PaymentGraft = graft.nuget.eShop.PaymentProcessor.PaymentApi;
using WebhookGraft = graft.nuget.Webhooks.API.WebhookEvents;

namespace eShop.Ordering.API.Application.IntegrationEvents;

internal static class GraftCalls
{
    private static readonly object Gate = new();
    private static bool ready;

    public static void OrderStarted(string userId)
    {
        Ensure();
        BasketGraft.OnOrderStarted(userId ?? string.Empty);
    }

    public static string AwaitingValidation(int orderId, string stockItemsJson)
    {
        Ensure();
        return CatalogGraft.OnOrderAwaitingValidation(orderId, stockItemsJson ?? string.Empty);
    }

    public static string Payment(int orderId)
    {
        Ensure();
        return PaymentGraft.OnStockConfirmed(orderId);
    }

    public static void OrderPaid(int orderId, string stockItemsJson)
    {
        Ensure();
        CatalogGraft.OnOrderPaid(orderId, stockItemsJson ?? string.Empty);
    }

    public static void OrderPaidWebhook(int orderId, string stockItemsJson)
    {
        Ensure();
        WebhookGraft.OnOrderPaid(orderId, stockItemsJson ?? string.Empty);
    }

    public static void OrderShipped(int orderId, string orderStatus, string buyerName)
    {
        Ensure();
        WebhookGraft.OnOrderShipped(orderId, orderStatus ?? string.Empty, buyerName ?? string.Empty);
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

            graft.nuget.Basket.API.GraftConfig.SetConfig(GraftRabbit.ClientConfig("graft.nuget.Basket.API", "eshop.basket"));
            graft.nuget.Catalog.API.GraftConfig.SetConfig(GraftRabbit.ClientConfig("graft.nuget.Catalog.API", "eshop.catalog"));
            graft.nuget.PaymentProcessor.GraftConfig.SetConfig(GraftRabbit.ClientConfig("graft.nuget.PaymentProcessor", "eshop.payment"));
            graft.nuget.Webhooks.API.GraftConfig.SetConfig(GraftRabbit.ClientConfig("graft.nuget.Webhooks.API", "eshop.webhooks"));
            ready = true;
        }
    }
}
