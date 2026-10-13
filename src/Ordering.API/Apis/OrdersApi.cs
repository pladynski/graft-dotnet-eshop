using System.Globalization;
using Graftcode.Context;
using Microsoft.AspNetCore.Http.HttpResults;
using CardType = eShop.Ordering.API.Application.Queries.CardType;
using Order = eShop.Ordering.API.Application.Queries.Order;

namespace eShop.Ordering.API;

public static class OrdersApi
{
    internal static RouteGroupBuilder MapOrdersApiV1(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("api/orders").HasApiVersion(1.0);

        api.MapPut("/cancel", CancelOrderAsync);
        api.MapPut("/ship", ShipOrderAsync);
        api.MapGet("{orderId:int}", GetOrderAsync);
        api.MapGet("/", GetOrdersByUserAsync);
        api.MapGet("/cardtypes", GetCardTypesAsync);
        api.MapPost("/draft", CreateOrderDraftAsync);
        api.MapPost("/", CreateOrderAsync);

        return api;
    }

    internal static async Task<Results<Ok, BadRequest<string>, ProblemHttpResult>> CancelOrderAsync(
        [FromHeader(Name = "x-requestid")] Guid requestId,
        CancelOrderCommand command,
        [AsParameters] OrderServices services)
    {
        if (requestId == Guid.Empty)
        {
            return TypedResults.BadRequest("Empty GUID is not valid for request ID");
        }

        var requestCancelOrder = new IdentifiedCommand<CancelOrderCommand, bool>(command, requestId);

        services.Logger.LogInformation(
            "Sending command: {CommandName} - {IdProperty}: {CommandId} ({@Command})",
            requestCancelOrder.GetGenericTypeName(),
            nameof(requestCancelOrder.Command.OrderNumber),
            requestCancelOrder.Command.OrderNumber,
            requestCancelOrder);

        var commandResult = await services.Mediator.Send(requestCancelOrder);

        if (!commandResult)
        {
            return TypedResults.Problem(detail: "Cancel order failed to process.", statusCode: 500);
        }

        return TypedResults.Ok();
    }

    internal static async Task<Results<Ok, BadRequest<string>, ProblemHttpResult>> ShipOrderAsync(
        [FromHeader(Name = "x-requestid")] Guid requestId,
        ShipOrderCommand command,
        [AsParameters] OrderServices services)
    {
        if (requestId == Guid.Empty)
        {
            return TypedResults.BadRequest("Empty GUID is not valid for request ID");
        }

        var requestShipOrder = new IdentifiedCommand<ShipOrderCommand, bool>(command, requestId);

        services.Logger.LogInformation(
            "Sending command: {CommandName} - {IdProperty}: {CommandId} ({@Command})",
            requestShipOrder.GetGenericTypeName(),
            nameof(requestShipOrder.Command.OrderNumber),
            requestShipOrder.Command.OrderNumber,
            requestShipOrder);

        var commandResult = await services.Mediator.Send(requestShipOrder);

        if (!commandResult)
        {
            return TypedResults.Problem(detail: "Ship order failed to process.", statusCode: 500);
        }

        return TypedResults.Ok();
    }

    internal static async Task<Results<Ok<Order>, NotFound>> GetOrderAsync(int orderId, [AsParameters] OrderServices services)
    {
        try
        {
            var order = await services.Queries.GetOrderAsync(orderId);
            return TypedResults.Ok(order);
        }
        catch
        {
            return TypedResults.NotFound();
        }
    }

    internal static async Task<Ok<IEnumerable<OrderSummary>>> GetOrdersByUserAsync([AsParameters] OrderServices services)
    {
        var userId = services.IdentityService.GetUserIdentity();
        var orders = await services.Queries.GetOrdersFromUserAsync(userId);
        return TypedResults.Ok(orders);
    }

    internal static async Task<Ok<IEnumerable<CardType>>> GetCardTypesAsync(IOrderQueries orderQueries)
    {
        var cardTypes = await orderQueries.GetCardTypesAsync();
        return TypedResults.Ok(cardTypes);
    }

    internal static async Task<OrderDraftDTO> CreateOrderDraftAsync(CreateOrderDraftCommand command, [AsParameters] OrderServices services)
    {
        services.Logger.LogInformation(
            "Sending command: {CommandName} - {IdProperty}: {CommandId} ({@Command})",
            command.GetGenericTypeName(),
            nameof(command.BuyerId),
            command.BuyerId,
            command);

        return await services.Mediator.Send(command);
    }

    internal static async Task<Results<Ok, BadRequest<string>>> CreateOrderAsync(
        [FromHeader(Name = "x-requestid")] Guid requestId,
        CreateOrderRequest request,
        [AsParameters] OrderServices services)
    {
        
        //mask the credit card number
        
        services.Logger.LogInformation(
            "Sending command: {CommandName} - {IdProperty}: {CommandId}",
            request.GetGenericTypeName(),
            nameof(request.UserId),
            request.UserId); //don't log the request as it has CC number

        if (requestId == Guid.Empty)
        {
            services.Logger.LogWarning("Invalid IntegrationEvent - RequestId is missing - {@IntegrationEvent}", request);
            return TypedResults.BadRequest("RequestId is missing.");
        }

        using (services.Logger.BeginScope(new List<KeyValuePair<string, object>> { new("IdentifiedCommandId", requestId) }))
        {
            var maskedCCNumber = request.CardNumber.Substring(request.CardNumber.Length - 4).PadLeft(request.CardNumber.Length, 'X');
            var createOrderCommand = new CreateOrderCommand(request.Items, request.UserId, request.UserName, request.City, request.Street,
                request.State, request.Country, request.ZipCode,
                maskedCCNumber, request.CardHolderName, request.CardExpiration,
                request.CardSecurityNumber, request.CardTypeId);

            var requestCreateOrder = new IdentifiedCommand<CreateOrderCommand, bool>(createOrderCommand, requestId);

            services.Logger.LogInformation(
                "Sending command: {CommandName} - {IdProperty}: {CommandId} ({@Command})",
                requestCreateOrder.GetGenericTypeName(),
                nameof(requestCreateOrder.Id),
                requestCreateOrder.Id,
                requestCreateOrder);

            var result = await services.Mediator.Send(requestCreateOrder);

            if (result)
            {
                services.Logger.LogInformation("CreateOrderCommand succeeded - RequestId: {RequestId}", requestId);
            }
            else
            {
                services.Logger.LogWarning("CreateOrderCommand failed - RequestId: {RequestId}", requestId);
            }

            return TypedResults.Ok();
        }
    }

    // Buyer id is the verified token subject. These methods do not accept a buyer id argument.
    public static OrderList ListOrders()
    {
        var authorization = CurrentAuthorization();
        return Run(async provider =>
        {
            var caller = await AuthorizeAsync(provider, authorization).ConfigureAwait(false);
            if (!caller.IsUser)
            {
                return new OrderList(0, [], [], [], [], caller.Failure ?? "unauthenticated", null);
            }

            var queries = provider.GetRequiredService<IOrderQueries>();
            var rows = (await queries.GetOrdersFromUserAsync(caller.Subject).ConfigureAwait(false)).ToList();
            return new OrderList(
                rows.Count,
                rows.Select(row => row.OrderNumber).ToArray(),
                rows.Select(row => row.Date.ToString("o")).ToArray(),
                rows.Select(row => row.Status ?? string.Empty).ToArray(),
                rows.Select(row => row.Total).ToArray(),
                "ok",
                null);
        });
    }

    public static OrderHit GetOrder(int orderId)
    {
        var authorization = CurrentAuthorization();
        return Run(async provider =>
        {
            var caller = await AuthorizeAsync(provider, authorization).ConfigureAwait(false);
            if (!caller.IsUser)
            {
                return Miss(caller.Failure ?? "unauthenticated");
            }

            var context = provider.GetRequiredService<OrderingContext>();
            var order = await context.Orders
                .Include(row => row.Buyer)
                .Include(row => row.OrderItems)
                .FirstOrDefaultAsync(row => row.Id == orderId)
                .ConfigureAwait(false);
            if (order is null)
            {
                return Miss("notFound");
            }

            if (!string.Equals(order.Buyer?.IdentityGuid, caller.Subject, StringComparison.Ordinal))
            {
                return Miss("forbidden");
            }

            var total = order.OrderItems.Sum(item => item.UnitPrice * item.Units);
            var hit = new OrderHit(1, order.Id, order.OrderDate.ToString("o"), order.OrderStatus.ToString(), (double)total, "ok", null);
            // Same fields the HTTP order document returned to MAUI. Callers skip Items when ItemCount is 0.
            var address = order.Address;
            hit.Street = address?.Street ?? string.Empty;
            hit.City = address?.City ?? string.Empty;
            hit.State = address?.State ?? string.Empty;
            hit.Country = address?.Country ?? string.Empty;
            hit.ZipCode = address?.ZipCode ?? string.Empty;
            var items = order.OrderItems?.ToList() ?? [];
            hit.ItemCount = items.Count;
            if (items.Count > 0)
            {
                hit.Items = items.Select(item => new OrderLine(
                    item.ProductId,
                    item.ProductName,
                    item.Units,
                    (double)item.UnitPrice,
                    item.PictureUrl)).ToArray();
            }

            return hit;
        });
    }

    public static OrderMutation CancelOrder(int orderId, string requestId)
    {
        var authorization = CurrentAuthorization();
        return Run(async provider =>
        {
            var caller = await AuthorizeAsync(provider, authorization).ConfigureAwait(false);
            if (!caller.IsUser)
            {
                return new OrderMutation(caller.Failure ?? "unauthenticated", null, 0);
            }

            if (!Guid.TryParse(requestId, out var parsed) || parsed == Guid.Empty)
            {
                return new OrderMutation("badRequest", "RequestId is missing.", 0);
            }

            var context = provider.GetRequiredService<OrderingContext>();
            var order = await context.Orders
                .Include(row => row.Buyer)
                .FirstOrDefaultAsync(row => row.Id == orderId)
                .ConfigureAwait(false);
            if (order is null)
            {
                return new OrderMutation("notFound", null, 0);
            }

            if (!string.Equals(order.Buyer?.IdentityGuid, caller.Subject, StringComparison.Ordinal))
            {
                return new OrderMutation("forbidden", null, 0);
            }

            var mediator = provider.GetRequiredService<IMediator>();
            var logger = provider.GetRequiredService<ILogger<OrderServices>>();
            var command = new IdentifiedCommand<CancelOrderCommand, bool>(new CancelOrderCommand(orderId), parsed);
            logger.LogInformation("Graft cancel order {OrderId} for {Buyer}", orderId, caller.Subject);
            var cancelled = await mediator.Send(command).ConfigureAwait(false);
            return cancelled
                ? new OrderMutation("cancelled", null, orderId)
                : new OrderMutation("badRequest", "Cancel order failed to process.", orderId);
        });
    }

    public static OrderMutation CreateOrder(
        string requestId,
        string userName,
        string city,
        string street,
        string state,
        string country,
        string zipCode,
        string cardNumber,
        string cardHolderName,
        string cardExpiration,
        string cardSecurityNumber,
        int cardTypeId,
        OrderLine[] lines)
    {
        var authorization = CurrentAuthorization();
        return Run(async provider =>
        {
            var caller = await AuthorizeAsync(provider, authorization).ConfigureAwait(false);
            if (!caller.IsUser)
            {
                return new OrderMutation(caller.Failure ?? "unauthenticated", null, 0);
            }

            if (!Guid.TryParse(requestId, out var parsed) || parsed == Guid.Empty)
            {
                return new OrderMutation("badRequest", "RequestId is missing.", 0);
            }

            if (!DateTime.TryParse(cardExpiration, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiration))
            {
                return new OrderMutation("badRequest", "Card expiration is invalid.", 0);
            }

            var rows = lines ?? [];
            if (rows.Length == 0 || rows.Any(row => row is null))
            {
                return new OrderMutation("badRequest", "Order items are missing.", 0);
            }

            var number = cardNumber ?? string.Empty;
            var masked = number.Length >= 4
                ? number.Substring(number.Length - 4).PadLeft(number.Length, 'X')
                : number;
            var items = new List<BasketItem>(rows.Length);
            foreach (var line in rows)
            {
                items.Add(new BasketItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ProductId = line.ProductId,
                    ProductName = line.ProductName ?? string.Empty,
                    UnitPrice = (decimal)line.UnitPrice,
                    Quantity = line.Units,
                    PictureUrl = line.PictureUrl ?? string.Empty
                });
            }

            var mediator = provider.GetRequiredService<IMediator>();
            var logger = provider.GetRequiredService<ILogger<OrderServices>>();
            var create = new CreateOrderCommand(
                items,
                caller.Subject,
                string.IsNullOrWhiteSpace(userName) ? caller.Subject : userName,
                city,
                street,
                state,
                country,
                zipCode,
                masked,
                cardHolderName,
                expiration,
                cardSecurityNumber,
                cardTypeId);
            var identified = new IdentifiedCommand<CreateOrderCommand, bool>(create, parsed);
            logger.LogInformation("Graft create order for {Buyer} request {RequestId}", caller.Subject, parsed);
            try
            {
                var created = await mediator.Send(identified).ConfigureAwait(false);
                if (!created)
                {
                    return new OrderMutation("badRequest", "Create order failed to process.", 0);
                }
            }
            catch (OrderingDomainException ex)
            {
                return new OrderMutation("badRequest", ex.Message, 0);
            }

            var context = provider.GetRequiredService<OrderingContext>();
            var orderId = await context.Orders
                .Where(row => row.Buyer.IdentityGuid == caller.Subject)
                .OrderByDescending(row => row.Id)
                .Select(row => row.Id)
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);
            return new OrderMutation("created", null, orderId);
        });
    }

    private static OrderHit Miss(string status) => new(0, 0, string.Empty, string.Empty, 0, status, null);

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

    private static Task<OrderingCaller> AuthorizeAsync(IServiceProvider provider, string authorization) =>
        OrderingTokens.ValidateAsync(provider.GetRequiredService<IConfiguration>(), authorization);

    private static readonly Lazy<Task<IHost>> HostTask = new(StartHostAsync);

    private static T Run<T>(Func<IServiceProvider, Task<T>> action) =>
        Task.Run(async () =>
        {
            var host = await HostTask.Value.ConfigureAwait(false);
            await using var scope = host.Services.CreateAsyncScope();
            return await action(scope.ServiceProvider).ConfigureAwait(false);
        }).GetAwaiter().GetResult();

    private static async Task<IHost> StartHostAsync()
    {
        // Same process can host basket (audience "basket") and orders (audience "orders").
        // Load this module's appsettings instead of the gateway working directory.
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = ResolveContentRoot(),
            ApplicationName = "Ordering.API"
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string>
        {
            ["EshopGraftHost"] = "true"
        });
        builder.AddServiceDefaults();
        builder.AddApplicationServices();

        if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("orderingdb")))
        {
            throw new InvalidOperationException(
                "OrdersApi requires ConnectionStrings__orderingdb. Aspire injects this for ordering-api; a standalone Gateway process needs it set.");
        }

        var host = builder.Build();
        await host.StartAsync().ConfigureAwait(false);
        return host;
    }

    private static string ResolveContentRoot()
    {
        var assemblyDir = Path.GetDirectoryName(typeof(OrdersApi).Assembly.Location);
        if (!string.IsNullOrEmpty(assemblyDir) && File.Exists(Path.Combine(assemblyDir, "appsettings.json")))
        {
            return assemblyDir;
        }

        return Directory.GetCurrentDirectory();
    }
}

// Classes, not positional records. gg 1.4.9 fails the NuGet build for records.
// Same constructor shapes so these can switch back to records later.
public sealed class OrderList
{
    public OrderList()
    {
    }

    public OrderList(int count, int[] orderNumbers, string[] dates, string[] statuses, double[] totals, string status, string detail)
    {
        Count = count;
        OrderNumbers = orderNumbers ?? [];
        Dates = dates ?? [];
        Statuses = statuses ?? [];
        Totals = totals ?? [];
        Status = status ?? string.Empty;
        Detail = detail ?? string.Empty;
    }

    public int Count { get; set; }

    // gg 1.4.9: an empty int[] property throws RuntimeBinderException
    // (Cannot convert type 'object[]' to 'int[]'). Callers skip these arrays when Count is 0.
    public int[] OrderNumbers { get; set; } = [];

    public string[] Dates { get; set; } = [];

    public string[] Statuses { get; set; } = [];

    public double[] Totals { get; set; } = [];

    public string Status { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;
}

public sealed class OrderHit
{
    public OrderHit()
    {
    }

    public OrderHit(int found, int orderNumber, string date, string orderStatus, double total, string status, string detail)
    {
        Found = found;
        OrderNumber = orderNumber;
        Date = date ?? string.Empty;
        OrderStatus = orderStatus ?? string.Empty;
        Total = total;
        Status = status ?? string.Empty;
        Detail = detail ?? string.Empty;
    }

    public int Found { get; set; }

    public int OrderNumber { get; set; }

    public string Date { get; set; } = string.Empty;

    public string OrderStatus { get; set; } = string.Empty;

    public double Total { get; set; }

    public string Status { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;

    public string Street { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public string Country { get; set; } = string.Empty;

    public string ZipCode { get; set; } = string.Empty;

    public int ItemCount { get; set; }

    // gg 1.4.9: an empty complex array throws in ComplexTypeResolver.Register.
    // Callers skip Items when ItemCount is 0.
    public OrderLine[] Items { get; set; } = [];
}

public sealed class OrderLine
{
    public OrderLine()
    {
    }

    public OrderLine(int productId, string productName, int units, double unitPrice, string pictureUrl)
    {
        ProductId = productId;
        ProductName = productName ?? string.Empty;
        Units = units;
        UnitPrice = unitPrice;
        PictureUrl = pictureUrl ?? string.Empty;
    }

    public int ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public int Units { get; set; }

    public double UnitPrice { get; set; }

    public string PictureUrl { get; set; } = string.Empty;
}

public sealed class OrderMutation
{
    public OrderMutation()
    {
    }

    public OrderMutation(string status, string detail, int orderNumber)
    {
        Status = status ?? string.Empty;
        Detail = detail ?? string.Empty;
        OrderNumber = orderNumber;
    }

    public string Status { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;

    public int OrderNumber { get; set; }
}

public record CreateOrderRequest(
    string UserId,
    string UserName,
    string City,
    string Street,
    string State,
    string Country,
    string ZipCode,
    string CardNumber,
    string CardHolderName,
    DateTime CardExpiration,
    string CardSecurityNumber,
    int CardTypeId,
    string Buyer,
    List<BasketItem> Items);
