// Graftcode catalog slice — thin facade. Public methods are the Gateway surface.
using System.Text.Json;
using Microsoft.Extensions.FileProviders;
using Pgvector.EntityFrameworkCore;

namespace eShop.Catalog.API;

/// <summary>
/// Public catalog methods hosted by Graftcode Gateway.
/// Each public method returns a JSON string (or a primitive) so callers are not
/// handed a generic object. Query and command bodies are the same ones the
/// legacy Minimal API wrappers use.
/// </summary>
public static class CatalogGraft
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Lazy<Task<IHost>> HostTask = new(StartHostAsync);

    public static int HostPid() => Environment.ProcessId;

    public static string GetItem(int id) =>
        Block(async services => id <= 0
            ? "null"
            : ToJson(await FindItemAsync(services, id).ConfigureAwait(false)));

    public static string ListItems(int pageIndex, int pageSize, string name, string typeIds, string brandIds) =>
        Block(async services => ToJson(await ListItemsAsync(
            services,
            pageIndex,
            pageSize,
            EmptyToNull(name),
            ParseIds(typeIds),
            ParseIds(brandIds)).ConfigureAwait(false)));

    public static string GetItemsByIds(string ids) =>
        Block(async services => ToJson(await FindItemsByIdsAsync(services, ParseIds(ids) ?? []).ConfigureAwait(false)));

    public static string Search(int pageIndex, int pageSize, string text) =>
        Block(async services => ToJson(await SearchAsync(services, pageIndex, pageSize, text ?? string.Empty).ConfigureAwait(false)));

    public static string ListBrands() =>
        Block(async services => ToJson(await ListBrandsAsync(services.Context).ConfigureAwait(false)));

    public static string ListTypes() =>
        Block(async services => ToJson(await ListTypesAsync(services.Context).ConfigureAwait(false)));

    public static string GetFacets(string typeIds, string brandIds) =>
        Block(async services => ToJson(await GetFacetsAsync(services, ParseIds(typeIds), ParseIds(brandIds)).ConfigureAwait(false)));

    public static string CreateItem(string itemJson) =>
        Block(async services =>
        {
            if (!TryReadItem(itemJson, out var product, out var error))
            {
                return ToJson(new ErrorPayload("badRequest", error));
            }

            var id = await CreateItemAsync(services, product).ConfigureAwait(false);
            return ToJson(new CreatedPayload(id));
        });

    public static string UpdateItem(int id, string itemJson) =>
        Block(async services =>
        {
            if (!TryReadItem(itemJson, out var product, out var error))
            {
                return ToJson(new ErrorPayload("badRequest", error));
            }

            var result = await UpdateItemAsync(services, id, product).ConfigureAwait(false);
            return result.StatusCode switch
            {
                StatusCodes.Status400BadRequest => ToJson(new ErrorPayload("badRequest", result.Detail ?? string.Empty)),
                StatusCodes.Status404NotFound => ToJson(new ErrorPayload("notFound", result.Detail ?? string.Empty)),
                _ => ToJson(new StatusPayload("updated"))
            };
        });

    public static string DeleteItem(int id) =>
        Block(async services => ToJson(new StatusPayload(
            await DeleteItemAsync(services, id).ConfigureAwait(false) ? "deleted" : "notFound")));

    public static string GetItemPicture(int id) =>
        Block(async (provider, services) =>
        {
            var root = provider.GetRequiredService<IWebHostEnvironment>().ContentRootPath;
            var picture = await TryGetPictureAsync(services.Context, root, id).ConfigureAwait(false);
            if (picture is null || !File.Exists(picture.Value.Path))
            {
                return "null";
            }

            var bytes = await File.ReadAllBytesAsync(picture.Value.Path).ConfigureAwait(false);
            return ToJson(new PicturePayload(picture.Value.Mime, Convert.ToBase64String(bytes)));
        });

    internal static async Task<PaginatedItems<CatalogItem>> ListItemsAsync(
        CatalogServices services,
        int pageIndex,
        int pageSize,
        string? name,
        int[]? type,
        int[]? brand)
    {
        var root = (IQueryable<CatalogItem>)services.Context.CatalogItems;

        if (name is not null)
        {
            root = root.Where(c => c.Name.StartsWith(name));
        }
        if (type is { Length: > 0 })
        {
            root = root.Where(c => type.Contains(c.CatalogTypeId));
        }
        if (brand is { Length: > 0 })
        {
            root = root.Where(c => brand.Contains(c.CatalogBrandId));
        }

        var totalItems = await root.LongCountAsync();

        var itemsOnPage = await root
            .Include(ci => ci.CatalogBrand)
            .OrderBy(c => c.Name)
            .Skip(pageSize * pageIndex)
            .Take(pageSize)
            .ToListAsync();

        return new PaginatedItems<CatalogItem>(pageIndex, pageSize, totalItems, itemsOnPage);
    }

    internal static async Task<CatalogFacets> GetFacetsAsync(CatalogServices services, int[]? type, int[]? brand)
    {
        var items = (IQueryable<CatalogItem>)services.Context.CatalogItems;

        // Brand counts are evaluated against the active type filter, and type counts against
        // the active brand filter. This mirrors the catalog's additive-within-facet,
        // intersect-across-facet selection semantics so each badge previews the result of
        // adding that option to the current selection.
        var brandScope = items;
        if (type is { Length: > 0 })
        {
            brandScope = brandScope.Where(c => type.Contains(c.CatalogTypeId));
        }
        var brandCounts = await brandScope
            .GroupBy(c => c.CatalogBrandId)
            .Select(g => new CatalogFacetCount(g.Key, g.Count()))
            .ToListAsync();

        var typeScope = items;
        if (brand is { Length: > 0 })
        {
            typeScope = typeScope.Where(c => brand.Contains(c.CatalogBrandId));
        }
        var typeCounts = await typeScope
            .GroupBy(c => c.CatalogTypeId)
            .Select(g => new CatalogFacetCount(g.Key, g.Count()))
            .ToListAsync();

        return new CatalogFacets(
            brandCounts,
            typeCounts,
            brandCounts.Sum(b => b.Count),
            typeCounts.Sum(t => t.Count));
    }

    internal static async Task<List<CatalogItem>> FindItemsByIdsAsync(CatalogServices services, int[] ids)
    {
        if (ids.Length == 0)
        {
            return [];
        }

        return await services.Context.CatalogItems.Where(item => ids.Contains(item.Id)).ToListAsync();
    }

    internal static async Task<CatalogItem?> FindItemAsync(CatalogServices services, int id) =>
        await services.Context.CatalogItems.Include(ci => ci.CatalogBrand).SingleOrDefaultAsync(ci => ci.Id == id);

    internal static Task<List<CatalogType>> ListTypesAsync(CatalogContext context) =>
        context.CatalogTypes.OrderBy(x => x.Type).ToListAsync();

    internal static Task<List<CatalogBrand>> ListBrandsAsync(CatalogContext context) =>
        context.CatalogBrands.OrderBy(x => x.Brand).ToListAsync();

    internal static async Task<PictureFile?> TryGetPictureAsync(CatalogContext context, string contentRootPath, int id)
    {
        var item = await context.CatalogItems.FindAsync(id);

        if (item is null || item.PictureFileName is null)
        {
            return null;
        }

        var path = GetFullPath(contentRootPath, item.PictureFileName);
        var extension = Path.GetExtension(item.PictureFileName) ?? string.Empty;
        var lastModified = File.GetLastWriteTimeUtc(path);
        return new PictureFile(path, MimeFromExtension(extension), lastModified);
    }

    internal static async Task<PaginatedItems<CatalogItem>> SearchAsync(
        CatalogServices services,
        int pageIndex,
        int pageSize,
        string text)
    {
        if (!services.CatalogAI.IsEnabled)
        {
            return await ListItemsAsync(services, pageIndex, pageSize, text, null, null);
        }

        var vector = await services.CatalogAI.GetEmbeddingAsync(text);

        if (vector is null)
        {
            return await ListItemsAsync(services, pageIndex, pageSize, text, null, null);
        }

        var totalItems = await services.Context.CatalogItems.LongCountAsync();

        List<CatalogItem> itemsOnPage;
        if (services.Logger.IsEnabled(LogLevel.Debug))
        {
            var itemsWithDistance = await services.Context.CatalogItems
                .Where(c => c.Embedding != null)
                .Select(c => new { Item = c, Distance = c.Embedding!.CosineDistance(vector) })
                .OrderBy(c => c.Distance)
                .Skip(pageSize * pageIndex)
                .Take(pageSize)
                .ToListAsync();

            services.Logger.LogDebug("Results from {text}: {results}", text, string.Join(", ", itemsWithDistance.Select(i => $"{i.Item.Name} => {i.Distance}")));

            itemsOnPage = itemsWithDistance.Select(i => i.Item).ToList();
        }
        else
        {
            itemsOnPage = await services.Context.CatalogItems
                .Where(c => c.Embedding != null)
                .OrderBy(c => c.Embedding!.CosineDistance(vector))
                .Skip(pageSize * pageIndex)
                .Take(pageSize)
                .ToListAsync();
        }

        return new PaginatedItems<CatalogItem>(pageIndex, pageSize, totalItems, itemsOnPage);
    }

    internal static async Task<MutationResult> UpdateItemAsync(CatalogServices services, int id, CatalogItem productToUpdate)
    {
        ArgumentNullException.ThrowIfNull(productToUpdate);

        var catalogItem = await services.Context.CatalogItems.SingleOrDefaultAsync(i => i.Id == id);

        if (catalogItem == null)
        {
            return MutationResult.NotFound($"Item with id {id} not found.");
        }

        var catalogEntry = services.Context.Entry(catalogItem);
        catalogEntry.CurrentValues.SetValues(productToUpdate);

        catalogItem.Embedding = await services.CatalogAI.GetEmbeddingAsync(catalogItem);

        var priceEntry = catalogEntry.Property(i => i.Price);

        if (priceEntry.IsModified)
        {
            var priceChangedEvent = new ProductPriceChangedIntegrationEvent(catalogItem.Id, productToUpdate.Price, priceEntry.OriginalValue);
            await services.EventService.SaveEventAndCatalogContextChangesAsync(priceChangedEvent);
            await services.EventService.PublishThroughEventBusAsync(priceChangedEvent);
        }
        else
        {
            await services.Context.SaveChangesAsync();
        }

        return MutationResult.Created(id);
    }

    internal static async Task<int> CreateItemAsync(CatalogServices services, CatalogItem product)
    {
        var item = new CatalogItem(product.Name)
        {
            Id = product.Id,
            CatalogBrandId = product.CatalogBrandId,
            CatalogTypeId = product.CatalogTypeId,
            Description = product.Description,
            PictureFileName = product.PictureFileName,
            Price = product.Price,
            AvailableStock = product.AvailableStock,
            RestockThreshold = product.RestockThreshold,
            MaxStockThreshold = product.MaxStockThreshold
        };
        item.Embedding = await services.CatalogAI.GetEmbeddingAsync(item);

        services.Context.CatalogItems.Add(item);
        await services.Context.SaveChangesAsync();
        return item.Id;
    }

    internal static async Task<bool> DeleteItemAsync(CatalogServices services, int id)
    {
        var item = services.Context.CatalogItems.SingleOrDefault(x => x.Id == id);

        if (item is null)
        {
            return false;
        }

        services.Context.CatalogItems.Remove(item);
        await services.Context.SaveChangesAsync();
        return true;
    }

    internal static string GetFullPath(string contentRootPath, string pictureFileName) =>
        Path.Combine(contentRootPath, "Pics", pictureFileName);

    internal readonly record struct PictureFile(string Path, string Mime, DateTime LastModified);

    internal readonly record struct MutationResult(int StatusCode, string? Detail, int Id)
    {
        public static MutationResult BadRequest(string detail) => new(StatusCodes.Status400BadRequest, detail, 0);
        public static MutationResult NotFound(string? detail) => new(StatusCodes.Status404NotFound, detail, 0);
        public static MutationResult Created(int id) => new(StatusCodes.Status201Created, null, id);
    }

    private static string Block(Func<CatalogServices, Task<string>> action) =>
        Block((_, services) => action(services));

    private static string Block(Func<IServiceProvider, CatalogServices, Task<string>> action) =>
        Task.Run(async () =>
        {
            var host = await HostTask.Value.ConfigureAwait(false);
            await using var scope = host.Services.CreateAsyncScope();
            var services = ActivatorUtilities.CreateInstance<CatalogServices>(scope.ServiceProvider);
            return await action(scope.ServiceProvider, services).ConfigureAwait(false);
        }).GetAwaiter().GetResult();

    private static async Task<IHost> StartHostAsync()
    {
        var contentRoot = ResolveContentRoot();
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = contentRoot,
            ApplicationName = "Catalog.API"
        });

        builder.Services.AddSingleton<IWebHostEnvironment>(sp => new GraftWebHostEnvironment(sp.GetRequiredService<IHostEnvironment>()));
        builder.AddServiceDefaults();
        builder.AddApplicationServices();

        if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("catalogdb")))
        {
            throw new InvalidOperationException(
                "CatalogGraft requires ConnectionStrings__catalogdb (Postgres with pgvector). " +
                "Aspire injects this for the legacy HTTP host; the Graftcode Gateway process does not.");
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
            if (File.Exists(Path.Combine(dir.FullName, "Catalog.API.csproj"))
                && File.Exists(Path.Combine(dir.FullName, "appsettings.json")))
            {
                return dir.FullName;
            }
        }

        return baseDir;
    }

    private static string ToJson<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    private static bool TryReadItem(string itemJson, out CatalogItem product, out string error)
    {
        product = null!;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(itemJson))
        {
            error = "Item body is required.";
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<CatalogItem>(itemJson, JsonOptions);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.Name))
            {
                error = "Item name is required.";
                return false;
            }

            product = parsed;
            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    internal static int[]? ParseIds(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return null;
        }

        var ids = new List<int>();
        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, out var id))
            {
                ids.Add(id);
            }
        }

        return ids.Count == 0 ? null : ids.ToArray();
    }

    private static string MimeFromExtension(string extension) => extension switch
    {
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".bmp" => "image/bmp",
        ".tiff" => "image/tiff",
        ".wmf" => "image/wmf",
        ".jp2" => "image/jp2",
        ".svg" => "image/svg+xml",
        ".webp" => "image/webp",
        _ => "application/octet-stream",
    };

    private sealed record ErrorPayload(string Status, string Detail);
    private sealed record CreatedPayload(int Id);
    private sealed record StatusPayload(string Status);
    private sealed record PicturePayload(string Mime, string Base64);

    private sealed class GraftWebHostEnvironment(IHostEnvironment inner) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = inner.ContentRootPath;
        public IFileProvider WebRootFileProvider { get; set; } = inner.ContentRootFileProvider;
        public string ApplicationName { get => inner.ApplicationName; set => inner.ApplicationName = value; }
        public IFileProvider ContentRootFileProvider { get => inner.ContentRootFileProvider; set => inner.ContentRootFileProvider = value; }
        public string ContentRootPath { get => inner.ContentRootPath; set => inner.ContentRootPath = value; }
        public string EnvironmentName { get => inner.EnvironmentName; set => inner.EnvironmentName = value; }
    }
}
