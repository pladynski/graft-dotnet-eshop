// Graftcode basket slice — gRPC overrides are public methods on BasketService.
using System.Text.Json;
using eShop.Basket.API.Model;
using eShop.Basket.API.Repositories;

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

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
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

    public static string GetBasket(string buyerId) =>
        Locked(() => Block(service => service.Read(buyerId)));

    public static string UpdateBasket(string buyerId, string itemsJson) =>
        Locked(() => Block(service => service.Replace(buyerId, itemsJson)));

    public static string DeleteBasket(string buyerId) =>
        Locked(() => Block(service => service.Remove(buyerId)));

    public static int HostPid() => Environment.ProcessId;

    internal async Task<string> Read(string buyerId)
    {
        if (string.IsNullOrEmpty(buyerId))
        {
            return "[]";
        }

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("GetBasket for {BuyerId}", buyerId);
        }

        var data = await repository.GetBasketAsync(buyerId);
        if (data?.Items is not { Count: > 0 })
        {
            return "[]";
        }

        return JsonSerializer.Serialize(data.Items.Select(Line), JsonOptions);
    }

    internal async Task<string> Replace(string buyerId, string itemsJson)
    {
        if (string.IsNullOrEmpty(buyerId))
        {
            return Status("unauthenticated");
        }

        List<BasketLine> lines;
        try
        {
            lines = JsonSerializer.Deserialize<List<BasketLine>>(itemsJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            return Status("badRequest", ex.Message);
        }

        var basket = new CustomerBasket(buyerId);
        foreach (var line in lines ?? [])
        {
            basket.Items.Add(new BasketItem
            {
                ProductId = line.ProductId,
                Quantity = line.Quantity
            });
        }

        var saved = await repository.UpdateBasketAsync(basket);
        if (saved is null)
        {
            return Status("notFound");
        }

        return JsonSerializer.Serialize((saved.Items ?? []).Select(Line), JsonOptions);
    }

    internal async Task<string> Remove(string buyerId)
    {
        if (string.IsNullOrEmpty(buyerId))
        {
            return Status("unauthenticated");
        }

        await repository.DeleteBasketAsync(buyerId);
        return Status("deleted");
    }

    private static BasketLine Line(BasketItem item) => new(item.ProductId, item.Quantity);

    private static string Status(string status, string detail = null) =>
        detail is null
            ? JsonSerializer.Serialize(new StatusPayload(status), JsonOptions)
            : JsonSerializer.Serialize(new ErrorPayload(status, detail), JsonOptions);

    private static string Locked(Func<string> action)
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

    private static string Block(Func<BasketService, Task<string>> action) =>
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

    private sealed record BasketLine(int ProductId, int Quantity);
    private sealed record StatusPayload(string Status);
    private sealed record ErrorPayload(string Status, string Detail);

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
