// Graftcode basket slice — original Grpc/BasketService. Redis logic stays here.
// Public methods replace the gRPC overrides. There is no Basket.BasketBase.
using eShop.Basket.API.IntegrationEvents.EventHandling;
using eShop.Basket.API.IntegrationEvents.EventHandling.Events;
using eShop.Basket.API.Model;
using eShop.Basket.API.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

namespace eShop.Basket.API;

public class BasketService
{
    private readonly IBasketRepository repository;
    private readonly ILogger<BasketService> logger;

    // Private so the generated graft only sees the public static methods.
    // A public constructor of IBasketRepository/ILogger makes the graft package fail to build.
    private BasketService(IBasketRepository repository, ILogger<BasketService> logger)
    {
        this.repository = repository;
        this.logger = logger;
    }

    private static readonly object Gate = new();
    private static readonly Lazy<Task<IHost>> HostTask = new(StartHostAsync);
    private static IServiceProvider AttachedServices;
    private static int AttachThread;

    internal static IDisposable Attach(IServiceProvider services)
    {
        Monitor.Enter(Gate);
        AttachedServices = services;
        AttachThread = Environment.CurrentManagedThreadId;
        return new AttachScope();
    }

    public static BasketResult GetBasket(string buyerId) =>
        Locked(() => Block(service => service.Read(buyerId)));

    public static BasketResult UpdateBasket(string buyerId, int[] productIds, int[] quantities) =>
        Locked(() => Block(service => service.Replace(buyerId, productIds, quantities)));

    public static BasketResult DeleteBasket(string buyerId) =>
        Locked(() => Block(service => service.Remove(buyerId)));

    public static BasketResult OnOrderStarted(string buyerId) =>
        Locked(() => Task.Run(async () =>
        {
            var provider = AttachedServices;
            if (provider is null)
            {
                var host = await HostTask.Value.ConfigureAwait(false);
                provider = host.Services;
            }

            await using var scope = provider.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IBasketRepository>();
            var logger = scope.ServiceProvider.GetService<ILogger<OrderStartedIntegrationEventHandler>>()
                ?? NullLogger<OrderStartedIntegrationEventHandler>.Instance;
            var handler = new OrderStartedIntegrationEventHandler(repository, logger);
            await handler.Handle(new OrderStartedIntegrationEvent(buyerId ?? string.Empty)).ConfigureAwait(false);
            return Status("deleted");
        }).GetAwaiter().GetResult());

    public static int HostPid() => Environment.ProcessId;

    internal async Task<BasketResult> Read(string buyerId)
    {
        if (string.IsNullOrEmpty(buyerId))
        {
            return Lines(null);
        }

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("GetBasket for {BuyerId}", buyerId);
        }

        var data = await repository.GetBasketAsync(buyerId);
        return Lines(data?.Items);
    }

    internal async Task<BasketResult> Replace(string buyerId, int[] productIds, int[] quantities)
    {
        if (string.IsNullOrEmpty(buyerId))
        {
            return Status("unauthenticated");
        }

        var ids = productIds ?? [];
        var quantitiesOrEmpty = quantities ?? [];
        var count = Math.Min(ids.Length, quantitiesOrEmpty.Length);
        var basket = new CustomerBasket(buyerId);
        for (var i = 0; i < count; i++)
        {
            basket.Items.Add(new BasketItem
            {
                ProductId = ids[i],
                Quantity = quantitiesOrEmpty[i]
            });
        }

        var saved = await repository.UpdateBasketAsync(basket);
        return saved is null ? Status("notFound") : Lines(saved.Items);
    }

    internal async Task<BasketResult> Remove(string buyerId)
    {
        if (string.IsNullOrEmpty(buyerId))
        {
            return Status("unauthenticated");
        }

        await repository.DeleteBasketAsync(buyerId);
        return Status("deleted");
    }

    private static BasketResult Lines(IEnumerable<BasketItem> items)
    {
        var list = items?.ToList() ?? [];
        return new BasketResult(
            list.Count,
            list.Select(item => item.ProductId).ToArray(),
            list.Select(item => item.Quantity).ToArray(),
            "ok",
            null);
    }

    private static BasketResult Status(string status) => new(0, [], [], status, null);

    private static T Locked<T>(Func<T> action)
    {
        var mine = Environment.CurrentManagedThreadId == AttachThread;
        if (!mine)
        {
            Monitor.Enter(Gate);
        }

        try
        {
            return action();
        }
        finally
        {
            if (!mine)
            {
                Monitor.Exit(Gate);
            }
        }
    }

    private static T Block<T>(Func<BasketService, Task<T>> action) =>
        Task.Run(async () =>
        {
            var provider = AttachedServices;
            if (provider is null)
            {
                var host = await HostTask.Value.ConfigureAwait(false);
                provider = host.Services;
            }

            await using var scope = provider.CreateAsyncScope();
            var service = new BasketService(
                scope.ServiceProvider.GetRequiredService<IBasketRepository>(),
                scope.ServiceProvider.GetRequiredService<ILogger<BasketService>>());
            return await action(service).ConfigureAwait(false);
        }).GetAwaiter().GetResult();

    private static async Task<IHost> StartHostAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string>
        {
            ["EshopGraftHost"] = "true"
        });
        builder.AddBasicServiceDefaults();
        builder.AddApplicationServices();

        if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("redis")))
        {
            throw new InvalidOperationException(
                "BasketService requires ConnectionStrings__redis. Aspire injects this for basket-api; a standalone Gateway process needs it set.");
        }

        var host = builder.Build();
        await host.StartAsync().ConfigureAwait(false);
        return host;
    }

    private sealed class AttachScope : IDisposable
    {
        public void Dispose()
        {
            AttachedServices = null;
            AttachThread = 0;
            Monitor.Exit(Gate);
        }
    }
}

public sealed record BasketResult(int Count, int[] ProductIds, int[] Quantities, string Status, string Detail);
