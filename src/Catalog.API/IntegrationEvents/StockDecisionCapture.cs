// Graftcode catalog slice — stock handlers still publish; the graft method returns that decision.
using System.Text.Json;

namespace eShop.Catalog.API.IntegrationEvents;

internal static class StockDecisionCapture
{
    private static readonly AsyncLocal<IntegrationEvent?> Pending = new();

    public static void Arm() => Pending.Value = null;

    public static void Record(IntegrationEvent integrationEvent) => Pending.Value = integrationEvent;

    public static string ToJson(JsonSerializerOptions options)
    {
        var integrationEvent = Pending.Value;
        Pending.Value = null;
        return integrationEvent switch
        {
            OrderStockRejectedIntegrationEvent rejected => JsonSerializer.Serialize(
                new Decision(
                    "rejected",
                    rejected.OrderStockItems.Where(item => !item.HasStock).Select(item => item.ProductId).ToArray()),
                options),
            OrderStockConfirmedIntegrationEvent => JsonSerializer.Serialize(new Decision("confirmed", []), options),
            _ => JsonSerializer.Serialize(new Decision("none", []), options)
        };
    }

    private sealed record Decision(string Result, int[] ProductIds);
}
