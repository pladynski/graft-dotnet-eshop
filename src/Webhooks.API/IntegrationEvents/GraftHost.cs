// Shared DI host for the graft methods on the webhook handlers. Not a graft type.
namespace Webhooks.API.IntegrationEvents;

internal static class GraftHost
{
    private static readonly Lazy<Task<IHost>> HostTask = new(StartHostAsync);

    internal static string Block(Func<IServiceProvider, Task<string>> action) =>
        Task.Run(async () =>
        {
            var host = await HostTask.Value.ConfigureAwait(false);
            await using var scope = host.Services.CreateAsyncScope();
            return await action(scope.ServiceProvider).ConfigureAwait(false);
        }).GetAwaiter().GetResult();

    internal static string Ok() => "{\"status\":\"ok\"}";

    internal static List<OrderStockItem> ReadStock(string stockItemsJson)
    {
        if (string.IsNullOrWhiteSpace(stockItemsJson))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<OrderStockItem>>(stockItemsJson, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
    }

    private static async Task<IHost> StartHostAsync()
    {
        var contentRoot = ResolveContentRoot();
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = contentRoot,
            ApplicationName = "Webhooks.API"
        });

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string>
        {
            ["EshopGraftHost"] = "true"
        });
        builder.AddServiceDefaults();
        builder.AddApplicationServices();

        if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("webhooksdb")))
        {
            throw new InvalidOperationException(
                "Webhooks graft host requires ConnectionStrings__webhooksdb. Aspire injects this for webhooks-api; a standalone Gateway process needs it set.");
        }

        var host = builder.Build();
        await host.StartAsync().ConfigureAwait(false);
        return host;
    }

    private static string ResolveContentRoot()
    {
        var baseDir = AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(baseDir, "appsettings.json")))
        {
            return baseDir;
        }

        var dir = new DirectoryInfo(baseDir);
        for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Webhooks.API.csproj"))
                && File.Exists(Path.Combine(dir.FullName, "appsettings.json")))
            {
                return dir.FullName;
            }
        }

        return baseDir;
    }
}
