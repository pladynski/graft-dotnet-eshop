using eShop.OrderProcessor;
using eShop.OrderProcessor.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace eShop.Application.UnitTests;

[TestClass]
public class OrderProcessorTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task ConfirmsGracePeriodForEveryEligibleOrder()
    {
        var repository = Substitute.For<IGracePeriodOrdersRepository>();
        repository.GetConfirmedGracePeriodOrdersAsync(
                TimeSpan.FromMinutes(1),
                Arg.Any<CancellationToken>())
            .Returns([12, 34]);
        var seen = new List<int>();
        var previous = OrderingLifecycle.GracePeriodConfirmed;
        OrderingLifecycle.GracePeriodConfirmed = seen.Add;
        var service = new GracePeriodManagerService(
            Options.Create(new BackgroundTaskOptions
            {
                GracePeriodTime = 1,
                CheckUpdateTime = 30
            }),
            NullLogger<GracePeriodManagerService>.Instance,
            repository);

        try
        {
            await service.CheckConfirmedGracePeriodOrders(TestContext.CancellationToken);
        }
        finally
        {
            OrderingLifecycle.GracePeriodConfirmed = previous;
        }

        Assert.HasCount(2, seen);
        Assert.AreEqual(12, seen[0]);
        Assert.AreEqual(34, seen[1]);
    }
}
