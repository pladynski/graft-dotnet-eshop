using System.Globalization;
using System.Net;
using eShop.ClientApp.Exceptions;
using eShop.ClientApp.Models.Basket;
using eShop.ClientApp.Models.Orders;
using eShop.ClientApp.Services.Identity;
using eShop.ClientApp.Services.RequestProvider;
using eShop.ClientApp.Services.Settings;
using graft.nuget.Ordering.API;
using OrderHit = graft.nuget.eShop.Ordering.API.OrderHit;
using OrderLine = graft.nuget.eShop.Ordering.API.OrderLine;
using OrderList = graft.nuget.eShop.Ordering.API.OrderList;
using OrdersApi = graft.nuget.eShop.Ordering.API.OrdersApi;

namespace eShop.ClientApp.Services.Order;

// Same order calls as the HTTP client. The access token is the maui user's token, sent per call.
// x-requestid is still a new guid for each create or cancel.
public class OrderService : IOrderService
{
    private readonly IIdentityService _identityService;
    private readonly ISettingsService _settingsService;

    public OrderService(IIdentityService identityService, ISettingsService settingsService)
    {
        _identityService = identityService;
        _settingsService = settingsService;
    }

    public async Task CreateOrderAsync(Models.Orders.Order newOrder)
    {
        var authToken = await _identityService.GetAuthTokenAsync().ConfigureAwait(false);

        if (string.IsNullOrEmpty(authToken))
        {
            return;
        }

        var lines = newOrder?.OrderItems ?? [];
        var result = Invoke(authToken, () => OrdersApi.CreateOrder(
            Guid.NewGuid().ToString("D"),
            newOrder?.UserName ?? string.Empty,
            newOrder?.ShippingCity ?? string.Empty,
            newOrder?.ShippingStreet ?? string.Empty,
            newOrder?.ShippingState ?? string.Empty,
            newOrder?.ShippingCountry ?? string.Empty,
            newOrder?.ShippingZipCode ?? string.Empty,
            newOrder?.CardNumber ?? string.Empty,
            newOrder?.CardHolderName ?? string.Empty,
            (newOrder?.CardExpiration ?? default).ToString("o", CultureInfo.InvariantCulture),
            newOrder?.CardSecurityNumber ?? string.Empty,
            newOrder?.CardTypeId ?? 0,
            lines.Select(item => new OrderLine(
                (int)item.ProductId,
                item.ProductName ?? string.Empty,
                item.Quantity,
                (double)item.UnitPrice,
                item.PictureUrl ?? string.Empty)).ToArray()));
        ThrowIfRejected(result?.Status, result?.Detail, result?.Status);
        if (!string.Equals(result?.Status, "created", StringComparison.Ordinal))
        {
            throw new HttpRequestExceptionEx(HttpStatusCode.BadRequest, result?.Detail ?? "Create order failed.");
        }
    }

    public async Task<IEnumerable<Models.Orders.Order>> GetOrdersAsync()
    {
        var authToken = await _identityService.GetAuthTokenAsync().ConfigureAwait(false);

        if (string.IsNullOrEmpty(authToken))
        {
            return Enumerable.Empty<Models.Orders.Order>();
        }

        var page = Invoke(authToken, () => OrdersApi.ListOrders());
        ThrowIfRejected(page?.Status, page?.Detail, page?.Status);
        return Lines(page);
    }

    public async Task<Models.Orders.Order> GetOrderAsync(int orderId)
    {
        var authToken = await _identityService.GetAuthTokenAsync().ConfigureAwait(false);

        if (string.IsNullOrEmpty(authToken))
        {
            return new Models.Orders.Order();
        }

        try
        {
            var hit = Invoke(authToken, () => OrdersApi.GetOrder(orderId));
            if (hit is null || hit.Found == 0 || !string.Equals(hit.Status, "ok", StringComparison.Ordinal))
            {
                return new Models.Orders.Order();
            }

            return Map(hit);
        }
        catch
        {
            return new Models.Orders.Order();
        }
    }

    public async Task<bool> CancelOrderAsync(int orderId)
    {
        var authToken = await _identityService.GetAuthTokenAsync().ConfigureAwait(false);

        if (string.IsNullOrEmpty(authToken))
        {
            return false;
        }

        try
        {
            var result = Invoke(authToken, () => OrdersApi.CancelOrder(orderId, Guid.NewGuid().ToString("D")));
            if (string.Equals(result?.Status, "badRequest", StringComparison.Ordinal))
            {
                return false;
            }

            ThrowIfRejected(result?.Status, result?.Detail, result?.Status);
            if (string.Equals(result?.Status, "notFound", StringComparison.Ordinal))
            {
                throw new HttpRequestExceptionEx(HttpStatusCode.NotFound, result?.Detail ?? "notFound");
            }

            if (!string.Equals(result?.Status, "cancelled", StringComparison.Ordinal))
            {
                throw new HttpRequestExceptionEx(HttpStatusCode.BadRequest, result?.Detail ?? result?.Status ?? "Cancel order failed.");
            }

            return true;
        }
        catch (HttpRequestExceptionEx ex) when (ex.HttpCode == HttpStatusCode.BadRequest)
        {
            return false;
        }
    }

    public OrderCheckout MapOrderToBasket(Models.Orders.Order order)
    {
        return new OrderCheckout
        {
            CardExpiration = order.CardExpiration,
            CardHolderName = order.CardHolderName,
            CardNumber = order.CardNumber,
            CardSecurityNumber = order.CardSecurityNumber,
            CardTypeId = order.CardTypeId,
            City = order.ShippingCity,
            State = order.ShippingState,
            Country = order.ShippingCountry,
            ZipCode = order.ShippingZipCode,
            Street = order.ShippingStreet
        };
    }

    private T Invoke<T>(string token, Func<T> call)
    {
        GraftConfig.Host = GraftHosts.Socket(_settingsService.GatewayOrdersEndpointBase);
        GraftConfig.Stateless = true;
        return GraftConfig.InvokeWithHeaders(call, new Dictionary<string, string>
        {
            ["Authorization"] = "Bearer " + token
        });
    }

    private static IEnumerable<Models.Orders.Order> Lines(OrderList page)
    {
        // gg 1.4.9: an empty int[] property throws. Do not read the arrays when Count is 0.
        if (page is null || page.Count <= 0)
        {
            return [];
        }

        var numbers = page.OrderNumbers;
        var dates = page.Dates;
        var statuses = page.Statuses;
        var totals = page.Totals;
        var count = Math.Min(page.Count, Math.Min(Math.Min(numbers.Length, dates.Length), Math.Min(statuses.Length, totals.Length)));
        var orders = new List<Models.Orders.Order>(count);
        for (var i = 0; i < count; i++)
        {
            orders.Add(new Models.Orders.Order
            {
                OrderNumber = numbers[i],
                OrderDate = DateTime.Parse(dates[i], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                OrderStatus = statuses[i],
                Total = (decimal)totals[i]
            });
        }

        return orders;
    }

    private static Models.Orders.Order Map(OrderHit hit)
    {
        var order = new Models.Orders.Order
        {
            OrderNumber = hit.OrderNumber,
            OrderDate = DateTime.Parse(hit.Date, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            OrderStatus = hit.OrderStatus,
            Total = (decimal)hit.Total,
            ShippingStreet = hit.Street ?? string.Empty,
            ShippingCity = hit.City ?? string.Empty,
            ShippingState = hit.State ?? string.Empty,
            ShippingCountry = hit.Country ?? string.Empty,
            ShippingZipCode = hit.ZipCode ?? string.Empty,
            OrderItems = ReadItems(hit)
        };
        return order;
    }

    private static List<OrderItem> ReadItems(OrderHit hit)
    {
        // gg 1.4.9: an empty complex array throws. Do not read Items when ItemCount is 0.
        if (hit is null || hit.ItemCount <= 0)
        {
            return [];
        }

        var rows = hit.Items;
        if (rows is null || rows.Length == 0)
        {
            return [];
        }

        var take = Math.Min(hit.ItemCount, rows.Length);
        var items = new List<OrderItem>(take);
        for (var i = 0; i < take; i++)
        {
            var row = rows[i];
            if (row is null)
            {
                continue;
            }

            items.Add(new OrderItem
            {
                ProductId = row.ProductId,
                ProductName = row.ProductName,
                Quantity = row.Units,
                UnitPrice = (decimal)row.UnitPrice,
                PictureUrl = row.PictureUrl
            });
        }

        return items;
    }

    private static void ThrowIfRejected(string status, string detail, string raw)
    {
        if (string.Equals(status, "unauthenticated", StringComparison.Ordinal)
            || string.Equals(status, "forbidden", StringComparison.Ordinal))
        {
            throw new ServiceAuthenticationException(string.IsNullOrEmpty(detail) ? raw : detail);
        }
    }
}
