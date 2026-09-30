using System.Text.Json;
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
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void GetBasketReturnsEmptyForNoUser()
    {
        var json = Call(Substitute.For<IBasketRepository>(), serviceBuyer => BasketService.GetBasket(""));

        Assert.AreEqual("[]", json);
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

        var lines = Lines(Call(repository, _ => BasketService.GetBasket("1")));

        Assert.HasCount(1, lines);
        Assert.AreEqual(7, lines[0].ProductId);
        Assert.AreEqual(2, lines[0].Quantity);
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

        Assert.AreEqual("[]", Call(repository, _ => BasketService.GetBasket("")));
    }

    [TestMethod]
    public async Task UpdateBasketPersistsItemsForAuthenticatedUser()
    {
        var repository = Substitute.For<IBasketRepository>();
        repository.UpdateBasketAsync(Arg.Any<CustomerBasket>())
            .Returns(call => call.Arg<CustomerBasket>());

        var lines = Lines(Call(repository, _ => BasketService.UpdateBasket("buyer-1", """[{"productId":42,"quantity":3}]""")));

        Assert.HasCount(1, lines);
        Assert.AreEqual(42, lines[0].ProductId);
        Assert.AreEqual(3, lines[0].Quantity);
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

        using var document = JsonDocument.Parse(Call(repository, _ => BasketService.UpdateBasket("", "[]")));

        Assert.AreEqual("unauthenticated", document.RootElement.GetProperty("status").GetString());
        await repository.DidNotReceive().UpdateBasketAsync(Arg.Any<CustomerBasket>());
    }

    [TestMethod]
    public async Task UpdateBasketReturnsNotFoundWhenRepositoryCannotPersist()
    {
        var repository = Substitute.For<IBasketRepository>();
        repository.UpdateBasketAsync(Arg.Any<CustomerBasket>())
            .Returns(Task.FromResult<CustomerBasket>(null!));

        using var document = JsonDocument.Parse(Call(repository, _ => BasketService.UpdateBasket("missing", "[]")));

        Assert.AreEqual("notFound", document.RootElement.GetProperty("status").GetString());
    }

    [TestMethod]
    public async Task DeleteBasketRemovesAuthenticatedUsersBasket()
    {
        var repository = Substitute.For<IBasketRepository>();

        using var document = JsonDocument.Parse(Call(repository, _ => BasketService.DeleteBasket("buyer-1")));

        Assert.AreEqual("deleted", document.RootElement.GetProperty("status").GetString());
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

    private static string Call(IBasketRepository repository, Func<IServiceProvider, string> action)
    {
        var services = new ServiceCollection();
        services.AddSingleton(repository);
        services.AddSingleton<ILogger<BasketService>>(NullLogger<BasketService>.Instance);
        services.AddSingleton<BasketService>();
        using var provider = services.BuildServiceProvider();
        using var _ = BasketService.Attach(provider);
        return action(provider);
    }

    private static List<BasketLine> Lines(string json) =>
        JsonSerializer.Deserialize<List<BasketLine>>(json, Json) ?? [];

    private sealed record BasketLine(int ProductId, int Quantity);
}
