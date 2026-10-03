// Graftcode basket slice — original Grpc/BasketService. Redis logic stays here.
// Public methods replace the gRPC overrides. There is no Basket.BasketBase.
// The caller is the verified JWT on this invocation, read from Graftcode.Context before any thread hop.
using eShop.Basket.API.IntegrationEvents.EventHandling;
using eShop.Basket.API.IntegrationEvents.EventHandling.Events;
using eShop.Basket.API.Model;
using eShop.Basket.API.Repositories;
using Graftcode.Context;
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

    public static BasketResult GetBasket()
    {
        var authorization = CurrentAuthorization();
        return Locked(() => Run(async provider =>
        {
            var caller = await AuthorizeAsync(provider, authorization, serviceOperation: false).ConfigureAwait(false);
            if (!caller.IsUser)
            {
                return caller.IsAnonymous ? Lines(null) : Status(caller.Failure ?? "unauthenticated");
            }

            return await Create(provider).Read(caller.Subject).ConfigureAwait(false);
        }));
    }

    public static BasketResult UpdateBasket(int[] productIds, int[] quantities)
    {
        var authorization = CurrentAuthorization();
        return Locked(() => Run(async provider =>
        {
            var caller = await AuthorizeAsync(provider, authorization, serviceOperation: false).ConfigureAwait(false);
            if (!caller.IsUser)
            {
                return Status("unauthenticated");
            }

            return await Create(provider).Replace(caller.Subject, productIds, quantities).ConfigureAwait(false);
        }));
    }

    public static BasketResult DeleteBasket()
    {
        var authorization = CurrentAuthorization();
        return Locked(() => Run(async provider =>
        {
            var caller = await AuthorizeAsync(provider, authorization, serviceOperation: false).ConfigureAwait(false);
            if (!caller.IsUser)
            {
                return Status("unauthenticated");
            }

            return await Create(provider).Remove(caller.Subject).ConfigureAwait(false);
        }));
    }

    public static BasketResult OnOrderStarted(string buyerId)
    {
        var authorization = CurrentAuthorization();
        return Locked(() => Run(async provider =>
        {
            var caller = await AuthorizeAsync(provider, authorization, serviceOperation: true).ConfigureAwait(false);
            if (!caller.IsService)
            {
                return Status(caller.Failure ?? "forbidden");
            }

            if (string.IsNullOrEmpty(buyerId))
            {
                return Status("unauthenticated");
            }

            var repository = provider.GetRequiredService<IBasketRepository>();
            var logger = provider.GetService<ILogger<OrderStartedIntegrationEventHandler>>()
                ?? NullLogger<OrderStartedIntegrationEventHandler>.Instance;
            var handler = new OrderStartedIntegrationEventHandler(repository, logger);
            await handler.Handle(new OrderStartedIntegrationEvent(buyerId)).ConfigureAwait(false);
            return Status("deleted");
        }));
    }

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

    // RequestContext.Current is thread-static. Read it on the graft entry thread, before Task.Run.
    private static string CurrentAuthorization()
    {
        var current = RequestContext.Current;
        if (current is null)
        {
            return null;
        }

        var headers = current.GetHeaders();
        if (headers is null)
        {
            return null;
        }

        if (headers.TryGetValue("authorization", out var value) && !string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (headers.TryGetValue("Authorization", out value) && !string.IsNullOrEmpty(value))
        {
            return value;
        }

        return null;
    }

    private static Task<BasketCaller> AuthorizeAsync(IServiceProvider provider, string authorization, bool serviceOperation) =>
        BasketTokens.ValidateAsync(provider.GetRequiredService<IConfiguration>(), authorization, serviceOperation);

    private static BasketService Create(IServiceProvider provider) =>
        new(
            provider.GetRequiredService<IBasketRepository>(),
            provider.GetRequiredService<ILogger<BasketService>>());

    private static T Run<T>(Func<IServiceProvider, Task<T>> action) =>
        Task.Run(async () =>
        {
            var provider = AttachedServices;
            if (provider is null)
            {
                var host = await HostTask.Value.ConfigureAwait(false);
                provider = host.Services;
            }

            await using var scope = provider.CreateAsyncScope();
            return await action(scope.ServiceProvider).ConfigureAwait(false);
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
