using System.Text.Json;
using eShop.Graft;

namespace eShop.Ordering.API.Application.IntegrationEvents;

public class OrderingIntegrationEventService(
    OrderingContext orderingContext,
    IIntegrationEventLogService integrationEventLogService,
    ILogger<OrderingIntegrationEventService> logger,
    IServiceScopeFactory scopeFactory) : IOrderingIntegrationEventService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly OrderingContext _orderingContext = orderingContext ?? throw new ArgumentNullException(nameof(orderingContext));
    private readonly IIntegrationEventLogService _eventLogService = integrationEventLogService ?? throw new ArgumentNullException(nameof(integrationEventLogService));
    private readonly ILogger<OrderingIntegrationEventService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));

    public async Task PublishEventsThroughEventBusAsync(Guid transactionId)
    {
        var pendingLogEvents = await _eventLogService.RetrieveEventLogsPendingToPublishAsync(transactionId);

        foreach (var logEvt in pendingLogEvents)
        {
            _logger.LogInformation("Publishing integration event: {IntegrationEventId} - ({@IntegrationEvent})", logEvt.EventId, logEvt.IntegrationEvent);

            try
            {
                await _eventLogService.MarkEventAsInProgressAsync(logEvt.EventId);
                await DispatchAsync(logEvt.IntegrationEvent);
                await _eventLogService.MarkEventAsPublishedAsync(logEvt.EventId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error publishing integration event: {IntegrationEventId}", logEvt.EventId);

                await _eventLogService.MarkEventAsFailedAsync(logEvt.EventId);
            }
        }
    }

    public async Task AddAndSaveEventAsync(IntegrationEvent evt)
    {
        _logger.LogInformation("Enqueuing integration event {IntegrationEventId} to repository ({@IntegrationEvent})", evt.Id, evt);

        await _eventLogService.SaveEventAsync(evt, _orderingContext.GetCurrentTransaction());
    }

    private async Task DispatchAsync(IntegrationEvent integrationEvent)
    {
        if (!GraftRabbit.TransportEnabled)
        {
            _logger.LogWarning(
                "Skipping graft dispatch for {EventType}. Set {Variable}=rabbitmq and run gg with RabbitmqPlugin.",
                integrationEvent.GetType().Name,
                GraftRabbit.TransportVariable);
            return;
        }

        switch (integrationEvent)
        {
            case OrderStartedIntegrationEvent started:
                GraftCalls.OrderStarted(started.UserId);
                break;
            case OrderStatusChangedToAwaitingValidationIntegrationEvent awaiting:
                var decisionJson = GraftCalls.AwaitingValidation(awaiting.OrderId, StockJson(awaiting.OrderStockItems));
                await ApplyStockDecisionAsync(awaiting.OrderId, decisionJson);
                break;
            case OrderStatusChangedToStockConfirmedIntegrationEvent confirmed:
                var outcome = GraftCalls.Payment(confirmed.OrderId);
                await ApplyPaymentOutcomeAsync(confirmed.OrderId, outcome);
                break;
            case OrderStatusChangedToPaidIntegrationEvent paid:
                var stockJson = StockJson(paid.OrderStockItems);
                GraftCalls.OrderPaid(paid.OrderId, stockJson);
                GraftCalls.OrderPaidWebhook(paid.OrderId, stockJson);
                break;
            case OrderStatusChangedToShippedIntegrationEvent shipped:
                GraftCalls.OrderShipped(shipped.OrderId, shipped.OrderStatus.ToString(), shipped.BuyerName);
                break;
            case OrderStatusChangedToSubmittedIntegrationEvent:
            case OrderStatusChangedToCancelledIntegrationEvent:
                // The storefront polls. A Gateway process does not own the Blazor circuits.
                break;
            default:
                _logger.LogInformation("No graft subscriber for {EventType}", integrationEvent.GetType().Name);
                break;
        }
    }

    private async Task ApplyStockDecisionAsync(int orderId, string decisionJson)
    {
        var decision = JsonSerializer.Deserialize<StockDecision>(decisionJson, JsonOptions)
            ?? throw new InvalidOperationException("Catalog stock graft returned an empty decision.");
        await using var scope = _scopeFactory.CreateAsyncScope();
        if (string.Equals(decision.Result, "confirmed", StringComparison.OrdinalIgnoreCase))
        {
            await OrderingApi.ApplyStockConfirmedAsync(scope.ServiceProvider, orderId);
            return;
        }

        if (string.Equals(decision.Result, "rejected", StringComparison.OrdinalIgnoreCase))
        {
            await OrderingApi.ApplyStockRejectedAsync(scope.ServiceProvider, orderId, decision.ProductIds ?? []);
            return;
        }

        throw new InvalidOperationException($"Catalog stock graft returned '{decision.Result}' for order {orderId}.");
    }

    private async Task ApplyPaymentOutcomeAsync(int orderId, string outcome)
    {
        var value = (outcome ?? string.Empty).Trim().Trim('"');
        await using var scope = _scopeFactory.CreateAsyncScope();
        if (string.Equals(value, "succeeded", StringComparison.OrdinalIgnoreCase))
        {
            await OrderingApi.ApplyPaymentSucceededAsync(scope.ServiceProvider, orderId);
            return;
        }

        if (string.Equals(value, "failed", StringComparison.OrdinalIgnoreCase))
        {
            await OrderingApi.ApplyPaymentFailedAsync(scope.ServiceProvider, orderId);
            return;
        }

        throw new InvalidOperationException($"Payment graft returned '{outcome}' for order {orderId}.");
    }

    private static string StockJson(IEnumerable<OrderStockItem> items) =>
        JsonSerializer.Serialize(
            (items ?? []).Select(item => new StockLine(item.ProductId, item.Units)),
            JsonOptions);

    private sealed record StockDecision(string Result, int[] ProductIds);
    private sealed record StockLine(int ProductId, int Units);
}
