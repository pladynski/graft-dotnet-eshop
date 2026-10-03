// Graftcode basket slice — OrderStarted is BasketService.OnOrderStarted, not a consumer.
using eShop.Basket.API.Repositories;

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
    }
}
