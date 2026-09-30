using eShop.Graft;
using OrderingGraft = graft.nuget.eShop.Ordering.API.OrderingApi;

namespace eShop.OrderProcessor.Services;

internal static class OrderingLifecycle
{
    private static readonly object Gate = new();
    private static bool ready;

    internal static Action<int> GracePeriodConfirmed { get; set; } = Confirm;

    private static void Confirm(int orderId)
    {
        if (!GraftRabbit.TransportEnabled)
        {
            throw new InvalidOperationException(
                "Set ESHOP_GRAFT_TRANSPORT=rabbitmq so grace period calls OrderingApi.OnGracePeriodConfirmed.");
        }

        Ensure();
        OrderingGraft.OnGracePeriodConfirmed(orderId);
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

            graft.nuget.Ordering.API.GraftConfig.SetConfig(GraftRabbit.ClientConfig("graft.nuget.Ordering.API", "eshop.ordering"));
            ready = true;
        }
    }
}
