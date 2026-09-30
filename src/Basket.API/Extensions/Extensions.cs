// Graftcode basket slice — gateway host does not consume the shared event queue.
using System.Text.Json.Serialization;
using eShop.Basket.API.Repositories;
using eShop.Basket.API.IntegrationEvents.EventHandling;
using eShop.Basket.API.IntegrationEvents.EventHandling.Events;

namespace eShop.Basket.API.Extensions;

public static class Extensions
{
    public static void AddApplicationServices(this IHostApplicationBuilder builder)
    {
        if (!builder.Configuration.GetValue("EshopGraftHost", false))
        {
            builder.AddDefaultAuthentication();
        }

        builder.AddRedisClient("redis");

        builder.Services.AddSingleton<IBasketRepository, RedisBasketRepository>();

        if (!builder.Configuration.GetValue("EshopGraftHost", false))
        {
            builder.AddRabbitMqEventBus("eventbus")
                   .AddSubscription<OrderStartedIntegrationEvent, OrderStartedIntegrationEventHandler>()
                   .ConfigureJsonOptions(options => options.TypeInfoResolverChain.Add(IntegrationEventContext.Default));
        }
    }
}

[JsonSerializable(typeof(OrderStartedIntegrationEvent))]
partial class IntegrationEventContext : JsonSerializerContext
{

}
