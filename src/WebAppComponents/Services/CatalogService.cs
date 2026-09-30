// Graftcode catalog slice — same CatalogService, calls the generated Catalog graft.
using System.Text.Json;
using eShop.WebAppComponents.Catalog;
using graft.nuget.Catalog.API;
using CatalogApi = graft.nuget.eShop.Catalog.API.CatalogApi;

namespace eShop.WebAppComponents.Services;

public class CatalogService : ICatalogService
{
    public const string DefaultHost = "ws://localhost:8000/ws";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    static CatalogService() => Configure();

    public static void Configure()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("CATALOG_GRAFT_TRANSPORT"), "rabbitmq", StringComparison.OrdinalIgnoreCase))
        {
            GraftConfig.SetConfig(ReadPluginConfig());
            return;
        }

        var socket = Environment.GetEnvironmentVariable("CATALOG_GRAFT_HOST");
        GraftConfig.Host = string.IsNullOrWhiteSpace(socket) ? DefaultHost : socket.Trim();
        GraftConfig.Stateless = true;
    }

    public Task<CatalogItem?> GetCatalogItem(int id) =>
        Task.FromResult(Parse<CatalogItem>(CatalogApi.GetItem(id)));

    public Task<CatalogResult> GetCatalogItems(int pageIndex, int pageSize, int[]? brands, int[]? types) =>
        Task.FromResult(Parse<CatalogResult>(CatalogApi.ListItems(pageIndex, pageSize, string.Empty, Join(types), Join(brands)))!);

    public Task<List<CatalogItem>> GetCatalogItems(IEnumerable<int> ids) =>
        Task.FromResult(Parse<List<CatalogItem>>(CatalogApi.GetItemsByIds(Join(ids)))!);

    public Task<CatalogResult> GetCatalogItemsWithSemanticRelevance(int page, int take, string text) =>
        Task.FromResult(Parse<CatalogResult>(CatalogApi.Search(page, take, text ?? string.Empty))!);

    public Task<IEnumerable<CatalogBrand>> GetBrands() =>
        Task.FromResult<IEnumerable<CatalogBrand>>(Parse<List<CatalogBrand>>(CatalogApi.ListBrands())!);

    public Task<IEnumerable<CatalogItemType>> GetTypes() =>
        Task.FromResult<IEnumerable<CatalogItemType>>(Parse<List<CatalogItemType>>(CatalogApi.ListTypes())!);

    public Task<CatalogFacets> GetCatalogFacets(int[]? brands, int[]? types) =>
        Task.FromResult(Parse<CatalogFacets>(CatalogApi.GetFacets(Join(types), Join(brands)))!);

    public (string Mime, byte[] Bytes)? GetItemPicture(int id)
    {
        var picture = Parse<PicturePayload>(CatalogApi.GetItemPicture(id));
        if (picture is null || string.IsNullOrEmpty(picture.Base64))
        {
            return null;
        }

        return (picture.Mime, Convert.FromBase64String(picture.Base64));
    }

    private static string ReadPluginConfig()
    {
        var path = Environment.GetEnvironmentVariable("CATALOG_GRAFT_PLUGIN_CONFIG");
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            return File.ReadAllText(path);
        }

        var pluginHost = Environment.GetEnvironmentVariable("CATALOG_GRAFT_PLUGIN_HOST");
        if (string.IsNullOrWhiteSpace(pluginHost))
        {
            pluginHost = "localhost:5672";
        }

        return $$"""
        {
          "configurations": {
            "graft.nuget.Catalog.API": {
              "runtime": "netcore",
              "host": "{{pluginHost.Trim()}}",
              "stateless": true,
              "plugin": {
                "name": "RabbitmqPlugin",
                "queue": "eshop.catalog",
                "replyQueue": "eshop.catalog.reply",
                "user": "guest",
                "password": "guest",
                "vhost": "/",
                "rpcTimeoutMs": 30000
              }
            }
          }
        }
        """;
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
