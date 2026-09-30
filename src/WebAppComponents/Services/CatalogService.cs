// Graftcode catalog slice — same CatalogService, calls CatalogApi over the gateway.
using System.Text.Json;
using eShop.WebAppComponents.Catalog;
using Hypertube.Netcore.Sdk;
using Hypertube.Netcore.Utils.ConnectionData;

namespace eShop.WebAppComponents.Services;

public class CatalogService : ICatalogService
{
    public const string DefaultHost = "ws://localhost:8000/ws";
    private const string FacadeType = "eShop.Catalog.API.CatalogApi";
    private const string DefaultPluginConfig =
        """
        {
          "name": "RabbitmqPlugin",
          "host": "localhost",
          "port": 5672,
          "queue": "eshop.catalog",
          "replyQueue": "eshop.catalog.reply",
          "user": "guest",
          "password": "guest",
          "vhost": "/",
          "rpcTimeoutMs": 30000
        }
        """;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly object _gate = new();
    private InvocationContext? _facade;

    public Task<CatalogItem?> GetCatalogItem(int id) =>
        Task.FromResult(Parse<CatalogItem>(Call("GetItem", id)));

    public Task<CatalogResult> GetCatalogItems(int pageIndex, int pageSize, int[]? brands, int[]? types) =>
        Task.FromResult(Parse<CatalogResult>(Call(
            "ListItems",
            pageIndex,
            pageSize,
            string.Empty,
            Join(types),
            Join(brands)))!);

    public Task<List<CatalogItem>> GetCatalogItems(IEnumerable<int> ids) =>
        Task.FromResult(Parse<List<CatalogItem>>(Call("GetItemsByIds", Join(ids)))!);

    public Task<CatalogResult> GetCatalogItemsWithSemanticRelevance(int page, int take, string text) =>
        Task.FromResult(Parse<CatalogResult>(Call("Search", page, take, text ?? string.Empty))!);

    public Task<IEnumerable<CatalogBrand>> GetBrands() =>
        Task.FromResult<IEnumerable<CatalogBrand>>(Parse<List<CatalogBrand>>(Call("ListBrands"))!);

    public Task<IEnumerable<CatalogItemType>> GetTypes() =>
        Task.FromResult<IEnumerable<CatalogItemType>>(Parse<List<CatalogItemType>>(Call("ListTypes"))!);

    public Task<CatalogFacets> GetCatalogFacets(int[]? brands, int[]? types) =>
        Task.FromResult(Parse<CatalogFacets>(Call("GetFacets", Join(types), Join(brands)))!);

    public (string Mime, byte[] Bytes)? GetItemPicture(int id)
    {
        var picture = Parse<PicturePayload>(Call("GetItemPicture", id));
        if (picture is null || string.IsNullOrEmpty(picture.Base64))
        {
            return null;
        }

        return (picture.Mime, Convert.FromBase64String(picture.Base64));
    }

    private InvocationContext Facade()
    {
        if (_facade is not null)
        {
            return _facade;
        }

        lock (_gate)
        {
            _facade ??= Open();
        }

        return _facade;
    }

    private static InvocationContext Open()
    {
        var transport = Environment.GetEnvironmentVariable("CATALOG_GRAFT_TRANSPORT");
        if (string.Equals(transport, "rabbitmq", StringComparison.OrdinalIgnoreCase))
        {
            var pluginHost = Environment.GetEnvironmentVariable("CATALOG_GRAFT_PLUGIN_HOST");
            if (string.IsNullOrWhiteSpace(pluginHost))
            {
                pluginHost = "localhost:5672";
            }

            return RuntimeBridge.Plugin(new PluginConnectionData(pluginHost.Trim(), ReadPluginConfig()))
                .Netcore()
                .GetType(FacadeType)
                .Execute();
        }

        var socket = Environment.GetEnvironmentVariable("CATALOG_GRAFT_HOST");
        if (string.IsNullOrWhiteSpace(socket))
        {
            socket = DefaultHost;
        }

        return RuntimeBridge.WebSocket(new WsConnectionData(socket.Trim()))
            .Netcore()
            .GetType(FacadeType)
            .Execute();
    }

    private static string ReadPluginConfig()
    {
        var path = Environment.GetEnvironmentVariable("CATALOG_GRAFT_PLUGIN_CONFIG");
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            return File.ReadAllText(path);
        }

        return DefaultPluginConfig;
    }

    private string Call(string method, params object[] args)
    {
        var value = Facade().InvokeStaticMethod(method, args).Execute().GetValue();
        if (value is null)
        {
            return "null";
        }

        return value as string ?? Convert.ToString(value) ?? "null";
    }

    private static T? Parse<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "null")
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    private static string Join(IEnumerable<int>? ids) =>
        ids is null ? string.Empty : string.Join(',', ids);

    private sealed record PicturePayload(string Mime, string Base64);
}
