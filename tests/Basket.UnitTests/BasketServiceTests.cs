using eShop.Basket.API;
using eShop.Basket.API.IntegrationEvents.EventHandling;
using eShop.Basket.API.IntegrationEvents.EventHandling.Events;
using eShop.Basket.API.Model;
using eShop.Basket.API.Repositories;
using Graftcode.Context;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace eShop.Basket.UnitTests;

[TestClass]
public class BasketServiceTests
{
    private static TokenAuthority authority = null!;

    public TestContext TestContext { get; set; } = null!;

    [ClassInitialize]
    public static async Task StartAuthority(TestContext _)
    {
        authority = await TokenAuthority.StartAsync();
    }

    [ClassCleanup]
    public static async Task StopAuthority()
    {
        if (authority is not null)
        {
            await authority.DisposeAsync();
        }
    }

    [TestMethod]
    public void GetBasketReturnsEmptyForNoUser()
    {
        var repository = Substitute.For<IBasketRepository>();
        var result = Call(repository, null, () => BasketService.GetBasket());

        Assert.AreEqual(0, result.Count);
        Assert.AreEqual("ok", result.Status);
        repository.DidNotReceive().GetBasketAsync(Arg.Any<string>());
    }

    [TestMethod]
    public void GetBasketReturnsItemsForTheVerifiedSubject()
    {
        var repository = Substitute.For<IBasketRepository>();
        repository.GetBasketAsync("buyer-a").Returns(Task.FromResult(new CustomerBasket
        {
            BuyerId = "buyer-a",
            Items = [new BasketItem { Id = "some-id", ProductId = 7, Quantity = 2 }]
        }));

        var token = authority.Issue("buyer-a", BasketTokens.UserScope);
        var result = Call(repository, token, () => BasketService.GetBasket());

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(7, result.ProductIds[0]);
        Assert.AreEqual(2, result.Quantities[0]);
        repository.DidNotReceive().GetBasketAsync("buyer-b");
    }

    [TestMethod]
    public void GetBasketDoesNotReadWhenTheTokenIsInvalid()
    {
        var repository = Substitute.For<IBasketRepository>();
        repository.GetBasketAsync("buyer-a").Returns(Task.FromResult(new CustomerBasket
        {
            BuyerId = "buyer-a",
            Items = [new BasketItem { Id = "some-id", ProductId = 7, Quantity = 2 }]
        }));

        var token = authority.Issue("buyer-a", BasketTokens.UserScope, otherKey: true);
        var result = Call(repository, token, () => BasketService.GetBasket());

        Assert.AreEqual(0, result.Count);
        Assert.AreEqual("unauthenticated", result.Status);
        repository.DidNotReceive().GetBasketAsync(Arg.Any<string>());
    }

    [TestMethod]
    public async Task UpdateBasketPersistsItemsForTheVerifiedSubject()
    {
        var repository = Substitute.For<IBasketRepository>();
        repository.UpdateBasketAsync(Arg.Any<CustomerBasket>())
            .Returns(call => call.Arg<CustomerBasket>());

        var token = authority.Issue("buyer-1", "openid basket");
        var result = Call(repository, token, () => BasketService.UpdateBasket([42], [3]));

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(42, result.ProductIds[0]);
        Assert.AreEqual(3, result.Quantities[0]);
        await repository.Received(1).UpdateBasketAsync(Arg.Is<CustomerBasket>(basket =>
            basket.BuyerId == "buyer-1" &&
            basket.Items.Count == 1 &&
            basket.Items[0].ProductId == 42 &&
            basket.Items[0].Quantity == 3));
    }

    [TestMethod]
    public async Task UpdateBasketRejectsAnonymousUser()
    {
        var repository = Substitute.For<IBasketRepository>();

        var result = Call(repository, null, () => BasketService.UpdateBasket([], []));

        Assert.AreEqual("unauthenticated", result.Status);
        await repository.DidNotReceive().UpdateBasketAsync(Arg.Any<CustomerBasket>());
    }

    [TestMethod]
    public async Task UpdateBasketRejectsExpiredToken()
    {
        var repository = Substitute.For<IBasketRepository>();
        var token = authority.Issue("buyer-1", BasketTokens.UserScope, DateTime.UtcNow.AddMinutes(-10));

        var result = Call(repository, token, () => BasketService.UpdateBasket([1], [1]));

        Assert.AreEqual("unauthenticated", result.Status);
        await repository.DidNotReceive().UpdateBasketAsync(Arg.Any<CustomerBasket>());
    }

    [TestMethod]
    public async Task UpdateBasketReturnsNotFoundWhenRepositoryCannotPersist()
    {
        var repository = Substitute.For<IBasketRepository>();
        repository.UpdateBasketAsync(Arg.Any<CustomerBasket>())
            .Returns(Task.FromResult<CustomerBasket>(null!));

        var token = authority.Issue("missing", BasketTokens.UserScope);
        var result = Call(repository, token, () => BasketService.UpdateBasket([], []));

        Assert.AreEqual("notFound", result.Status);
    }

    [TestMethod]
    public async Task DeleteBasketRemovesTheVerifiedUsersBasket()
    {
        var repository = Substitute.For<IBasketRepository>();
        var token = authority.Issue("buyer-1", BasketTokens.UserScope);

        var result = Call(repository, token, () => BasketService.DeleteBasket());

        Assert.AreEqual("deleted", result.Status);
        await repository.Received(1).DeleteBasketAsync("buyer-1");
    }

    [TestMethod]
    public async Task OnOrderStartedDeletesBasketForTheOrderingScope()
    {
        var repository = Substitute.For<IBasketRepository>();
        var token = authority.Issue("ordering", BasketTokens.ServiceScope);

        var result = Call(repository, token, () => BasketService.OnOrderStarted("buyer-1"));

        Assert.AreEqual("deleted", result.Status);
        await repository.Received(1).DeleteBasketAsync("buyer-1");
    }

    [TestMethod]
    public async Task UserScopeCannotDeleteAnotherBasketThroughOnOrderStarted()
    {
        var repository = Substitute.For<IBasketRepository>();
        var token = authority.Issue("buyer-a", BasketTokens.UserScope);

        var result = Call(repository, token, () => BasketService.OnOrderStarted("buyer-b"));

        Assert.AreEqual("forbidden", result.Status);
        await repository.DidNotReceive().DeleteBasketAsync(Arg.Any<string>());
    }

    [TestMethod]
    public async Task OrderStartedEventRemovesUsersBasket()
    {
        var repository = Substitute.For<IBasketRepository>();
        var handler = new OrderStartedIntegrationEventHandler(
            repository,
            NullLogger<OrderStartedIntegrationEventHandler>.Instance);

        await handler.Handle(new OrderStartedIntegrationEvent("buyer-1"));

        await repository.Received(1).DeleteBasketAsync("buyer-1");
    }

    private static BasketResult Call(IBasketRepository repository, string token, Func<BasketResult> action)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["Identity:Url"] = authority.Issuer,
                ["Identity:Audience"] = TokenAuthority.Audience
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton(repository);
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<ILogger<BasketService>>(NullLogger<BasketService>.Instance);
        using var provider = services.BuildServiceProvider();
        using var _ = BasketService.Attach(provider);
        UseAuthorization(token);
        try
        {
            return action();
        }
        finally
        {
            UseAuthorization(null);
        }
    }

    private static void UseAuthorization(string token)
    {
        var context = new RequestContext();
        var headers = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(token))
        {
            headers["authorization"] = "Bearer " + token;
        }

        context.SetHeaders(headers);
        RequestContext.Current = context;
    }
}
