// Graftcode catalog slice — original CatalogApi handlers are the public methods.
using Microsoft.Extensions.FileProviders;
using Pgvector.EntityFrameworkCore;

namespace eShop.Catalog.API;

/// <summary>
/// Catalog operations hosted by Graftcode Gateway.
/// Storefront reads and writes are classes. A page's Items is CatalogItemView[].
/// gg 1.4.9 still throws when a client reads an empty complex array, a null complex
/// value, or an empty int[] property. Those shapes use Found or a count and the
/// caller does not touch the failing member. There is no REST route table.
/// </summary>
public static class CatalogApi
{
    private static readonly Lazy<Task<IHost>> HostTask = new(StartHostAsync);
    private static IServiceProvider? AttachedServices;

    /// <summary>Use an existing DI host (tests) instead of starting a second one.</summary>
    internal static void Attach(IServiceProvider services) => AttachedServices = services;

    public static int HostPid() => Environment.ProcessId;

    public static CatalogItemHit GetItem(int id) =>
        Block(async services =>
        {
            if (id <= 0)
            {
                return new CatalogItemHit { Found = 0 };
            }

            var item = await FindItemAsync(services, id).ConfigureAwait(false);
            return item is null
                ? new CatalogItemHit { Found = 0 }
                : new CatalogItemHit { Found = 1, Item = ToView(item) };
        });

    public static CatalogPage ListItems(int pageIndex, int pageSize, string name, int[] typeIds, int[] brandIds) =>
        Block(async services => ToPage(await ListItemsAsync(
            services,
            pageIndex,
            pageSize,
            EmptyToNull(name),
            typeIds,
            brandIds).ConfigureAwait(false)));

    public static CatalogItemSet GetItemsByIds(int[] ids) =>
        Block(async services => ToSet(await FindItemsByIdsAsync(services, ids ?? []).ConfigureAwait(false)));

    public static CatalogPage Search(int pageIndex, int pageSize, string text) =>
        Block(async services => ToPage(await SearchAsync(services, pageIndex, pageSize, text ?? string.Empty).ConfigureAwait(false)));

    public static CatalogBrandList ListBrands() =>
        Block(async services => ToBrandList(await ListBrandsAsync(services.Context).ConfigureAwait(false)));

    public static CatalogTypeList ListTypes() =>
        Block(async services => ToTypeList(await ListTypesAsync(services.Context).ConfigureAwait(false)));

    public static CatalogFacetSet GetFacets(int[] typeIds, int[] brandIds) =>
        Block(async services => ToFacetSet(await GetFacetsAsync(services, typeIds, brandIds).ConfigureAwait(false)));

    public static CatalogMutation CreateItem(CatalogItemWrite item) =>
        Block(async services =>
        {
            if (!TryReadItem(item, out var product, out var error))
            {
                return new CatalogMutation { Status = "badRequest", Detail = error };
            }

            var id = await CreateItemAsync(services, product).ConfigureAwait(false);
            return new CatalogMutation { Status = "created", Id = id };
        });

    public static CatalogMutation UpdateItem(int id, CatalogItemWrite item) =>
        Block(async services =>
        {
            if (!TryReadItem(item, out var product, out var error))
            {
                return new CatalogMutation { Status = "badRequest", Detail = error };
            }

            var result = await UpdateItemAsync(services, id, product).ConfigureAwait(false);
            return result.StatusCode switch
            {
                StatusCodes.Status400BadRequest => new CatalogMutation { Status = "badRequest", Detail = result.Detail ?? string.Empty },
                StatusCodes.Status404NotFound => new CatalogMutation { Status = "notFound", Detail = result.Detail ?? string.Empty },
                _ => new CatalogMutation { Status = "updated", Id = id }
            };
        });

    public static CatalogMutation DeleteItem(int id) =>
        Block(async services => new CatalogMutation
        {
            Status = await DeleteItemAsync(services, id).ConfigureAwait(false) ? "deleted" : "notFound"
        });

    public static CatalogPicture GetItemPicture(int id) =>
        Block(async (provider, services) =>
        {
            var root = provider.GetRequiredService<IWebHostEnvironment>().ContentRootPath;
            var picture = await TryGetPictureAsync(services.Context, root, id).ConfigureAwait(false);
            if (picture is null || !File.Exists(picture.Value.Path))
            {
                return new CatalogPicture();
            }

            var bytes = await File.ReadAllBytesAsync(picture.Value.Path).ConfigureAwait(false);
            return new CatalogPicture
            {
                Mime = picture.Value.Mime,
                Base64 = Convert.ToBase64String(bytes)
            };
        });

    public static StockDecision OnOrderAwaitingValidation(int orderId, StockRequest stockItems) =>
        Block(async (provider, _) =>
        {
            var handler = ActivatorUtilities.CreateInstance<OrderStatusChangedToAwaitingValidationIntegrationEventHandler>(provider);
            return await handler.DecideAsync(new OrderStatusChangedToAwaitingValidationIntegrationEvent(orderId, ReadStockLines(stockItems))).ConfigureAwait(false);
        });

    public static GraftStatus OnOrderPaid(int orderId, StockRequest stockItems) =>
        Block(async (provider, _) =>
        {
            var handler = ActivatorUtilities.CreateInstance<OrderStatusChangedToPaidIntegrationEventHandler>(provider);
            await handler.Handle(new OrderStatusChangedToPaidIntegrationEvent(orderId, ReadStockLines(stockItems))).ConfigureAwait(false);
            return new GraftStatus("ok");
        });

    private static List<OrderStockItem> ReadStockLines(StockRequest stockItems)
    {
        var productIds = stockItems?.ProductIds ?? [];
        var units = stockItems?.Units ?? [];
        var count = Math.Min(productIds.Length, units.Length);
        var items = new List<OrderStockItem>(count);
        for (var i = 0; i < count; i++)
        {
            items.Add(new OrderStockItem(productIds[i], units[i]));
        }

        return items;
    }

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
            .Include(ci => ci.CatalogType)
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

        return await services.Context.CatalogItems
            .Include(item => item.CatalogBrand)
            .Include(item => item.CatalogType)
            .Where(item => ids.Contains(item.Id))
            .ToListAsync();
    }

    internal static async Task<CatalogItem?> FindItemAsync(CatalogServices services, int id) =>
        await services.Context.CatalogItems
            .Include(ci => ci.CatalogBrand)
            .Include(ci => ci.CatalogType)
            .SingleOrDefaultAsync(ci => ci.Id == id);

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
                .Include(c => c.CatalogBrand)
                .Include(c => c.CatalogType)
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
                .Include(c => c.CatalogBrand)
                .Include(c => c.CatalogType)
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

    private static T Block<T>(Func<CatalogServices, Task<T>> action) =>
        Block((_, services) => action(services));

    private static T Block<T>(Func<IServiceProvider, CatalogServices, Task<T>> action) =>
        Task.Run(async () =>
        {
            var provider = AttachedServices;
            if (provider is null)
            {
                var host = await HostTask.Value.ConfigureAwait(false);
                provider = host.Services;
            }

            await using var scope = provider.CreateAsyncScope();
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

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["EshopGraftHost"] = "true"
        });
        builder.Services.AddSingleton<IWebHostEnvironment>(sp => new CatalogHostEnvironment(sp.GetRequiredService<IHostEnvironment>()));
        builder.AddServiceDefaults();
        builder.AddApplicationServices();

        if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("catalogdb")))
        {
            throw new InvalidOperationException(
                "CatalogApi requires ConnectionStrings__catalogdb (Postgres with pgvector). " +
                "Aspire injects this when catalog-api starts; a standalone Gateway process needs it set.");
        }

        var host = builder.Build();
        await host.StartAsync().ConfigureAwait(false);
        return host;
    }

    private static string ResolveContentRoot()
    {
        var baseDir = AppContext.BaseDirectory;
        if (string.IsNullOrEmpty(baseDir))
        {
            // Gateway loads the module with an empty base directory.
            baseDir = Path.GetDirectoryName(typeof(CatalogApi).Assembly.Location) ?? string.Empty;
        }

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

    private static CatalogItemView ToView(CatalogItem item) => new()
    {
        Id = item.Id,
        Name = item.Name ?? string.Empty,
        Description = item.Description ?? string.Empty,
        Price = (double)item.Price,
        PictureFileName = item.PictureFileName ?? string.Empty,
        CatalogBrandId = item.CatalogBrandId,
        CatalogTypeId = item.CatalogTypeId,
        AvailableStock = item.AvailableStock,
        RestockThreshold = item.RestockThreshold,
        MaxStockThreshold = item.MaxStockThreshold,
        // gg 1.4.9: a null nested Brand/Type throws ArgumentNullException (parameter 'key')
        // in ComplexTypeResolver.Register when the client reads it. Always send an object.
        Brand = new CatalogBrandView
        {
            Id = item.CatalogBrandId,
            Brand = item.CatalogBrand?.Brand ?? string.Empty
        },
        Type = new CatalogTypeView
        {
            Id = item.CatalogTypeId,
            Type = item.CatalogType?.Type ?? string.Empty
        }
    };

    private static CatalogPage ToPage(PaginatedItems<CatalogItem> page)
    {
        var items = page.Data.Select(ToView).ToArray();
        var total = page.Count > int.MaxValue ? int.MaxValue : (int)page.Count;
        return new CatalogPage
        {
            PageIndex = page.PageIndex,
            PageSize = page.PageSize,
            Count = total,
            ItemCount = items.Length,
            Items = items
        };
    }

    private static CatalogItemSet ToSet(IEnumerable<CatalogItem> items)
    {
        var views = items.Select(ToView).ToArray();
        return new CatalogItemSet { Count = views.Length, Items = views };
    }

    private static CatalogBrandList ToBrandList(IEnumerable<CatalogBrand> brands)
    {
        var items = brands.Select(brand => new CatalogBrandView
        {
            Id = brand.Id,
            Brand = brand.Brand ?? string.Empty
        }).ToArray();
        return new CatalogBrandList { Count = items.Length, Items = items };
    }

    private static CatalogTypeList ToTypeList(IEnumerable<CatalogType> types)
    {
        var items = types.Select(type => new CatalogTypeView
        {
            Id = type.Id,
            Type = type.Type ?? string.Empty
        }).ToArray();
        return new CatalogTypeList { Count = items.Length, Items = items };
    }

    private static CatalogFacetSet ToFacetSet(CatalogFacets facets)
    {
        var brands = facets.BrandCounts.Select(count => new CatalogFacetView { Id = count.Id, Count = count.Count }).ToArray();
        var types = facets.TypeCounts.Select(count => new CatalogFacetView { Id = count.Id, Count = count.Count }).ToArray();
        return new CatalogFacetSet
        {
            BrandTotal = facets.BrandTotal,
            TypeTotal = facets.TypeTotal,
            BrandCount = brands.Length,
            TypeCount = types.Length,
            BrandCounts = brands,
            TypeCounts = types
        };
    }

    private static bool TryReadItem(CatalogItemWrite item, out CatalogItem product, out string error)
    {
        product = null!;
        error = string.Empty;
        if (item is null)
        {
            error = "Item body is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(item.Name))
        {
            error = "Item name is required.";
            return false;
        }

        product = new CatalogItem(item.Name)
        {
            Id = item.Id,
            Description = item.Description,
            Price = (decimal)item.Price,
            PictureFileName = string.IsNullOrEmpty(item.PictureFileName) ? null : item.PictureFileName,
            CatalogBrandId = item.CatalogBrandId,
            CatalogTypeId = item.CatalogTypeId,
            AvailableStock = item.AvailableStock,
            RestockThreshold = item.RestockThreshold,
            MaxStockThreshold = item.MaxStockThreshold
        };
        return true;
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

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

    private sealed class CatalogHostEnvironment(IHostEnvironment inner) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = inner.ContentRootPath;
        public IFileProvider WebRootFileProvider { get; set; } = inner.ContentRootFileProvider;
        public string ApplicationName { get => inner.ApplicationName; set => inner.ApplicationName = value; }
        public IFileProvider ContentRootFileProvider { get => inner.ContentRootFileProvider; set => inner.ContentRootFileProvider = value; }
        public string ContentRootPath { get => inner.ContentRootPath; set => inner.ContentRootPath = value; }
        public string EnvironmentName { get => inner.EnvironmentName; set => inner.EnvironmentName = value; }
    }
}

public sealed class StockRequest
{
    public StockRequest()
    {
    }

    public StockRequest(int[] productIds, int[] units)
    {
        ProductIds = productIds ?? [];
        Units = units ?? [];
    }

    public int[] ProductIds { get; set; } = [];

    public int[] Units { get; set; } = [];
}

public sealed class StockDecision
{
    public StockDecision()
    {
    }

    public StockDecision(string result, int[] productIds)
    {
        Result = result ?? string.Empty;
        ProductIds = productIds ?? [];
    }

    public string Result { get; set; } = string.Empty;

    // gg 1.4.9: an empty int[] property throws RuntimeBinderException
    // (Cannot convert type 'object[]' to 'int[]') in the generated getter.
    // Callers read ProductIds only when Result is rejected and the array is non-empty.
    public int[] ProductIds { get; set; } = [];
}

public sealed class GraftStatus
{
    public GraftStatus()
    {
    }

    public GraftStatus(string status)
    {
        Status = status ?? string.Empty;
    }

    public string Status { get; set; } = string.Empty;
}

public sealed class CatalogBrandView
{
    public int Id { get; set; }

    public string Brand { get; set; } = string.Empty;
}

public sealed class CatalogTypeView
{
    public int Id { get; set; }

    public string Type { get; set; } = string.Empty;
}

public sealed class CatalogItemView
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public double Price { get; set; }

    public string PictureFileName { get; set; } = string.Empty;

    public int CatalogBrandId { get; set; }

    public int CatalogTypeId { get; set; }

    public int AvailableStock { get; set; }

    public int RestockThreshold { get; set; }

    public int MaxStockThreshold { get; set; }

    public CatalogBrandView Brand { get; set; } = new();

    public CatalogTypeView Type { get; set; } = new();
}

public sealed class CatalogItemHit
{
    // gg 1.4.9: a null CatalogItemView return throws ArgumentNullException (parameter 'key')
    // in Hypertube.Netcore.Sdk.Tools.ComplexTypeResolver.Register. Found == 0 means missing.
    public int Found { get; set; }

    public CatalogItemView? Item { get; set; }
}

public sealed class CatalogPage
{
    public int PageIndex { get; set; }

    public int PageSize { get; set; }

    public int Count { get; set; }

    // gg 1.4.9: Item[] get_Items throws ArgumentNullException (parameter 'key') in
    // ComplexTypeResolver.Register when the array is empty. Callers skip Items when this is 0.
    public int ItemCount { get; set; }

    public CatalogItemView[] Items { get; set; } = [];
}

public sealed class CatalogItemSet
{
    // Same empty-array failure as CatalogPage.ItemCount. Skip Items when Count is 0.
    public int Count { get; set; }

    public CatalogItemView[] Items { get; set; } = [];
}

public sealed class CatalogBrandList
{
    // Same empty-array failure as CatalogPage.ItemCount. Skip Items when Count is 0.
    public int Count { get; set; }

    public CatalogBrandView[] Items { get; set; } = [];
}

public sealed class CatalogTypeList
{
    // Same empty-array failure as CatalogPage.ItemCount. Skip Items when Count is 0.
    public int Count { get; set; }

    public CatalogTypeView[] Items { get; set; } = [];
}

public sealed class CatalogFacetView
{
    public int Id { get; set; }

    public int Count { get; set; }
}

public sealed class CatalogFacetSet
{
    public int BrandTotal { get; set; }

    public int TypeTotal { get; set; }

    // Same empty-array failure as CatalogPage.ItemCount. Skip BrandCounts/TypeCounts when these are 0.
    public int BrandCount { get; set; }

    public int TypeCount { get; set; }

    public CatalogFacetView[] BrandCounts { get; set; } = [];

    public CatalogFacetView[] TypeCounts { get; set; } = [];
}

public sealed class CatalogPicture
{
    public string Mime { get; set; } = string.Empty;

    public string Base64 { get; set; } = string.Empty;
}

public sealed class CatalogItemWrite
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public double Price { get; set; }

    public string PictureFileName { get; set; } = string.Empty;

    public int CatalogBrandId { get; set; }

    public int CatalogTypeId { get; set; }

    public int AvailableStock { get; set; }

    public int RestockThreshold { get; set; }

    public int MaxStockThreshold { get; set; }
}

public sealed class CatalogMutation
{
    public string Status { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;

    public int Id { get; set; }
}
