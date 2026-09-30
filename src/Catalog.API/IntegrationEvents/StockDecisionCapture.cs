// Graftcode catalog slice — stock handlers still publish; the graft method returns that decision.
namespace eShop.Catalog.API.IntegrationEvents;

internal static class StockDecisionCapture
{
    // The list is allocated before the handler awaits. Recording after that await
    // mutates the same list; replacing the AsyncLocal value itself would not be
    // visible to the caller.
    private static readonly AsyncLocal<List<IntegrationEvent>?> Pending = new();

    public static void Arm() => Pending.Value = [];

    public static void Record(IntegrationEvent integrationEvent) => Pending.Value?.Add(integrationEvent);

    public static StockDecision Take()
    {
        var integrationEvent = Pending.Value?.LastOrDefault();
        Pending.Value = null;
        return integrationEvent switch
        {
            OrderStockRejectedIntegrationEvent rejected => new StockDecision(
                "rejected",
                rejected.OrderStockItems.Where(item => !item.HasStock).Select(item => item.ProductId).ToArray()),
            OrderStockConfirmedIntegrationEvent => new StockDecision("confirmed", []),
            _ => new StockDecision("none", [])
        };
    }
}
