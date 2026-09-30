// Generated grafts. RabbitmqPlugin today; another plugin is a config change, not a new call site.
using eShop.Graft;
using BasketGraft = graft.nuget.eShop.Basket.API.BasketService;
using CatalogGraft = graft.nuget.eShop.Catalog.API.CatalogApi;
using StockDecision = graft.nuget.eShop.Catalog.API.StockDecision;
using StockRequest = graft.nuget.eShop.Catalog.API.StockRequest;
using PaymentGraft = graft.nuget.eShop.PaymentProcessor.IntegrationEvents.EventHandling.OrderStatusChangedToStockConfirmedIntegrationEventHandler;
using PaidWebhook = graft.nuget.Webhooks.API.IntegrationEvents.OrderStatusChangedToPaidIntegrationEventHandler;
using ShippedWebhook = graft.nuget.Webhooks.API.IntegrationEvents.OrderStatusChangedToShippedIntegrationEventHandler;
using WebhookStock = graft.nuget.Webhooks.API.IntegrationEvents.StockRequest;

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

    public static StockDecision AwaitingValidation(int orderId, IEnumerable<OrderStockItem> items)
    {
        Ensure();
        var lines = (items ?? []).ToArray();
        var request = new StockRequest(
            lines.Select(item => item.ProductId).ToArray(),
            lines.Select(item => item.Units).ToArray());
        return CatalogGraft.OnOrderAwaitingValidation(orderId, request);
    }

    public static string Payment(int orderId)
    {
        Ensure();
        return PaymentGraft.OnStockConfirmed(orderId);
    }

    public static void OrderPaid(int orderId, IEnumerable<OrderStockItem> items)
    {
        Ensure();
        var lines = (items ?? []).ToArray();
        CatalogGraft.OnOrderPaid(orderId, new StockRequest(
            lines.Select(item => item.ProductId).ToArray(),
            lines.Select(item => item.Units).ToArray()));
    }

    public static void OrderPaidWebhook(int orderId, IEnumerable<OrderStockItem> items)
    {
        Ensure();
        var lines = (items ?? []).ToArray();
        PaidWebhook.OnOrderPaid(orderId, new WebhookStock(
            lines.Select(item => item.ProductId).ToArray(),
            lines.Select(item => item.Units).ToArray()));
    }

    public static void OrderShipped(int orderId, string orderStatus, string buyerName)
    {
        Ensure();
        ShippedWebhook.OnOrderShipped(orderId, orderStatus ?? string.Empty, buyerName ?? string.Empty);
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
