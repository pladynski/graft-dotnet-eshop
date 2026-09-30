using eShop.Basket.API;
using eShop.Basket.API.IntegrationEvents.EventHandling;
using eShop.Basket.API.IntegrationEvents.EventHandling.Events;
using eShop.Basket.API.Model;
using eShop.Basket.API.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace eShop.Basket.UnitTests;

[TestClass]
public class BasketServiceTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void GetBasketReturnsEmptyForNoUser()
    {
        var result = Call(Substitute.For<IBasketRepository>(), _ => BasketService.GetBasket(""));

        Assert.AreEqual(0, result.Count);
        Assert.AreEqual("ok", result.Status);
    }

    [TestMethod]
    public void GetBasketReturnsItemsForValidUserId()
    {
        var repository = Substitute.For<IBasketRepository>();
        repository.GetBasketAsync("1").Returns(Task.FromResult(new CustomerBasket
        {
            BuyerId = "1",
            Items = [new BasketItem { Id = "some-id", ProductId = 7, Quantity = 2 }]
        }));

        var result = Call(repository, _ => BasketService.GetBasket("1"));

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(7, result.ProductIds[0]);
        Assert.AreEqual(2, result.Quantities[0]);
    }

    [TestMethod]
    public void GetBasketReturnsEmptyForInvalidUserId()
    {
        var repository = Substitute.For<IBasketRepository>();
        repository.GetBasketAsync("1").Returns(Task.FromResult(new CustomerBasket
        {
            BuyerId = "1",
            Items = [new BasketItem { Id = "some-id", ProductId = 7, Quantity = 2 }]
        }));

        var result = Call(repository, _ => BasketService.GetBasket(""));

        Assert.AreEqual(0, result.Count);
        Assert.AreEqual("ok", result.Status);
    }

    [TestMethod]
    public async Task UpdateBasketPersistsItemsForAuthenticatedUser()
    {
        var repository = Substitute.For<IBasketRepository>();
        repository.UpdateBasketAsync(Arg.Any<CustomerBasket>())
            .Returns(call => call.Arg<CustomerBasket>());

        var result = Call(repository, _ => BasketService.UpdateBasket("buyer-1", [42], [3]));

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

        var result = Call(repository, _ => BasketService.UpdateBasket("", [], []));

        Assert.AreEqual("unauthenticated", result.Status);
        await repository.DidNotReceive().UpdateBasketAsync(Arg.Any<CustomerBasket>());
    }

    [TestMethod]
    public async Task UpdateBasketReturnsNotFoundWhenRepositoryCannotPersist()
    {
        var repository = Substitute.For<IBasketRepository>();
        repository.UpdateBasketAsync(Arg.Any<CustomerBasket>())
            .Returns(Task.FromResult<CustomerBasket>(null!));

        var result = Call(repository, _ => BasketService.UpdateBasket("missing", [], []));

        Assert.AreEqual("notFound", result.Status);
    }

    [TestMethod]
    public async Task DeleteBasketRemovesAuthenticatedUsersBasket()
    {
        var repository = Substitute.For<IBasketRepository>();

        var result = Call(repository, _ => BasketService.DeleteBasket("buyer-1"));

        Assert.AreEqual("deleted", result.Status);
        await repository.Received(1).DeleteBasketAsync("buyer-1");
    }

    [TestMethod]
    public async Task OnOrderStartedDeletesBasket()
    {
        var repository = Substitute.For<IBasketRepository>();

        var result = Call(repository, _ => BasketService.OnOrderStarted("buyer-1"));

        Assert.AreEqual("deleted", result.Status);
        await repository.Received(1).DeleteBasketAsync("buyer-1");
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

    private static BasketResult Call(IBasketRepository repository, Func<IServiceProvider, BasketResult> action)
    {
        var services = new ServiceCollection();
        services.AddSingleton(repository);
        services.AddSingleton<ILogger<BasketService>>(NullLogger<BasketService>.Instance);
        services.AddSingleton<BasketService>();
        using var provider = services.BuildServiceProvider();
        using var _ = BasketService.Attach(provider);
        return action(provider);
    }
}
