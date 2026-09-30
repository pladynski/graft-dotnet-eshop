// Graftcode catalog slice — same CatalogService, calls CatalogGraft instead of HTTP.
using System.Text.Json;
using eShop.WebAppComponents.Catalog;
using Hypertube.Netcore.Sdk;
using Hypertube.Netcore.Utils.ConnectionData;

namespace eShop.WebAppComponents.Services;

public class CatalogService : ICatalogService
{
    public const string DefaultHost = "ws://localhost:8000/ws";
    private const string FacadeType = "eShop.Catalog.API.CatalogGraft";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _host;
    private readonly object _gate = new();
    private InvocationContext? _facade;

    public CatalogService()
    {
        var configured = Environment.GetEnvironmentVariable("CATALOG_GRAFT_HOST");
        _host = string.IsNullOrWhiteSpace(configured) ? DefaultHost : configured.Trim();
    }

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

    private InvocationContext Facade()
    {
        if (_facade is not null)
        {
            return _facade;
        }

        lock (_gate)
        {
            _facade ??= RuntimeBridge.WebSocket(new WsConnectionData(_host))
                .Netcore()
                .GetType(FacadeType)
                .Execute();
        }

        return _facade;
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
}
