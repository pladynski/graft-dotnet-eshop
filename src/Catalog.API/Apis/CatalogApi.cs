// Graftcode catalog slice — HTTP maps stay as thin wrappers around CatalogGraft.
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace eShop.Catalog.API;

public static class CatalogApi
{
    public static IEndpointRouteBuilder MapCatalogApi(this IEndpointRouteBuilder app)
    {
        // RouteGroupBuilder for catalog endpoints
        var vApi = app.NewVersionedApi("Catalog");
        var api = vApi.MapGroup("api/catalog").HasApiVersion(1, 0).HasApiVersion(2, 0);
        var v1 = vApi.MapGroup("api/catalog").HasApiVersion(1, 0);
        var v2 = vApi.MapGroup("api/catalog").HasApiVersion(2, 0);

        // Routes for querying catalog items.
        v1.MapGet("/items", GetAllItemsV1)
            .WithName("ListItems")
            .WithSummary("List catalog items")
            .WithDescription("Get a paginated list of items in the catalog.")
            .WithTags("Items");
        v2.MapGet("/items", GetAllItems)
            .WithName("ListItems-V2")
            .WithSummary("List catalog items")
            .WithDescription("Get a paginated list of items in the catalog.")
            .WithTags("Items");
        api.MapGet("/items/by", GetItemsByIds)
            .WithName("BatchGetItems")
            .WithSummary("Batch get catalog items")
            .WithDescription("Get multiple items from the catalog")
            .WithTags("Items");
        api.MapGet("/items/{id:int}", GetItemById)
            .WithName("GetItem")
            .WithSummary("Get catalog item")
            .WithDescription("Get an item from the catalog")
            .WithTags("Items");
        v1.MapGet("/items/by/{name:minlength(1)}", GetItemsByName)
            .WithName("GetItemsByName")
            .WithSummary("Get catalog items by name")
            .WithDescription("Get a paginated list of catalog items with the specified name.")
            .WithTags("Items");
        api.MapGet("/items/{id:int}/pic", GetItemPictureById)
            .WithName("GetItemPicture")
            .WithSummary("Get catalog item picture")
            .WithDescription("Get the picture for a catalog item")
            .WithTags("Items");

        api.MapGet("/items/facets", GetCatalogFacets)
            .WithName("GetCatalogFacets")
            .WithSummary("Get catalog facet counts")
            .WithDescription("Get per-brand and per-type item counts for the current filter selection, used to drive the catalog filter badges without transferring every item to the client.")
            .WithTags("Items");

        // Routes for resolving catalog items using AI.
        v1.MapGet("/items/withsemanticrelevance/{text:minlength(1)}", GetItemsBySemanticRelevanceV1)
            .WithName("GetRelevantItems")
            .WithSummary("Search catalog for relevant items")
            .WithDescription("Search the catalog for items related to the specified text")
            .WithTags("Search");

                // Routes for resolving catalog items using AI.
        v2.MapGet("/items/withsemanticrelevance", GetItemsBySemanticRelevance)
            .WithName("GetRelevantItems-V2")
            .WithSummary("Search catalog for relevant items")
            .WithDescription("Search the catalog for items related to the specified text")
            .WithTags("Search");

        // Routes for resolving catalog items by type and brand.
        v1.MapGet("/items/type/{typeId}/brand/{brandId?}", GetItemsByBrandAndTypeId)
            .WithName("GetItemsByTypeAndBrand")
            .WithSummary("Get catalog items by type and brand")
            .WithDescription("Get catalog items of the specified type and brand")
            .WithTags("Types");
        v1.MapGet("/items/type/all/brand/{brandId:int?}", GetItemsByBrandId)
            .WithName("GetItemsByBrand")
            .WithSummary("List catalog items by brand")
            .WithDescription("Get a list of catalog items for the specified brand")
            .WithTags("Brands");
        api.MapGet("/catalogtypes",
            [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
            async (CatalogContext context) => await CatalogGraft.ListTypesAsync(context))
            .WithName("ListItemTypes")
            .WithSummary("List catalog item types")
            .WithDescription("Get a list of the types of catalog items")
            .WithTags("Types");
        api.MapGet("/catalogbrands",
            [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
            async (CatalogContext context) => await CatalogGraft.ListBrandsAsync(context))
            .WithName("ListItemBrands")
            .WithSummary("List catalog item brands")
            .WithDescription("Get a list of the brands of catalog items")
            .WithTags("Brands");

        // Routes for modifying catalog items.
        v1.MapPut("/items", UpdateItemV1)
            .WithName("UpdateItem")
            .WithSummary("Create or replace a catalog item")
            .WithDescription("Create or replace a catalog item")
            .WithTags("Items");
        v2.MapPut("/items/{id:int}", UpdateItem)
            .WithName("UpdateItem-V2")
            .WithSummary("Create or replace a catalog item")
            .WithDescription("Create or replace a catalog item")
            .WithTags("Items");
        api.MapPost("/items", CreateItem)
            .WithName("CreateItem")
            .WithSummary("Create a catalog item")
            .WithDescription("Create a new item in the catalog");
        api.MapDelete("/items/{id:int}", DeleteItemById)
            .WithName("DeleteItem")
            .WithSummary("Delete catalog item")
            .WithDescription("Delete the specified catalog item");

        return app;
    }

    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public static async Task<Ok<PaginatedItems<CatalogItem>>> GetAllItemsV1(
        [AsParameters] PaginationRequest paginationRequest,
        [AsParameters] CatalogServices services)
    {
        return await GetAllItems(paginationRequest, services, null, null, null);
    }

    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public static async Task<Ok<PaginatedItems<CatalogItem>>> GetAllItems(
        [AsParameters] PaginationRequest paginationRequest,
        [AsParameters] CatalogServices services,
        [Description("The name of the item to return")] string? name,
        [Description("The types of items to return. Repeat the parameter to filter by multiple types.")] int[]? type,
        [Description("The brands of items to return. Repeat the parameter to filter by multiple brands.")] int[]? brand)
    {
        var page = await CatalogGraft.ListItemsAsync(
            services,
            paginationRequest.PageIndex,
            paginationRequest.PageSize,
            name,
            type,
            brand);
        return TypedResults.Ok(page);
    }

    public static async Task<Ok<CatalogFacets>> GetCatalogFacets(
        [AsParameters] CatalogServices services,
        [Description("The types the counts should be evaluated within. Repeat the parameter to include multiple types.")] int[]? type,
        [Description("The brands the counts should be evaluated within. Repeat the parameter to include multiple brands.")] int[]? brand)
    {
        return TypedResults.Ok(await CatalogGraft.GetFacetsAsync(services, type, brand));
    }

    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public static async Task<Ok<List<CatalogItem>>> GetItemsByIds(
        [AsParameters] CatalogServices services,
        [Description("List of ids for catalog items to return")] int[] ids)
    {
        return TypedResults.Ok(await CatalogGraft.FindItemsByIdsAsync(services, ids));
    }

    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public static async Task<Results<Ok<CatalogItem>, NotFound, BadRequest<ProblemDetails>>> GetItemById(
        HttpContext httpContext,
        [AsParameters] CatalogServices services,
        [Description("The catalog item id")] int id)
    {
        if (id <= 0)
        {
            return TypedResults.BadRequest<ProblemDetails>(new (){
                Detail = "Id is not valid"
            });
        }

        var item = await CatalogGraft.FindItemAsync(services, id);
        return item is null ? TypedResults.NotFound() : TypedResults.Ok(item);
    }

    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public static async Task<Ok<PaginatedItems<CatalogItem>>> GetItemsByName(
        [AsParameters] PaginationRequest paginationRequest,
        [AsParameters] CatalogServices services,
        [Description("The name of the item to return")] string name)
    {
        return await GetAllItems(paginationRequest, services, name, null, null);
    }

    [ProducesResponseType<byte[]>(StatusCodes.Status200OK, "application/octet-stream",
        [ "image/png", "image/gif", "image/jpeg", "image/bmp", "image/tiff",
          "image/wmf", "image/jp2", "image/svg+xml", "image/webp" ])]
    public static async Task<Results<PhysicalFileHttpResult,NotFound>> GetItemPictureById(
        CatalogContext context,
        IWebHostEnvironment environment,
        [Description("The catalog item id")] int id)
    {
        var picture = await CatalogGraft.TryGetPictureAsync(context, environment.ContentRootPath, id);
        if (picture is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.PhysicalFile(picture.Value.Path, picture.Value.Mime, lastModified: picture.Value.LastModified);
    }

    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public static async Task<Results<Ok<PaginatedItems<CatalogItem>>, RedirectToRouteHttpResult>> GetItemsBySemanticRelevanceV1(
        [AsParameters] PaginationRequest paginationRequest,
        [AsParameters] CatalogServices services,
        [Description("The text string to use when search for related items in the catalog")] string text)

    {
        return await GetItemsBySemanticRelevance(paginationRequest, services, text);
    }

    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public static async Task<Results<Ok<PaginatedItems<CatalogItem>>, RedirectToRouteHttpResult>> GetItemsBySemanticRelevance(
        [AsParameters] PaginationRequest paginationRequest,
        [AsParameters] CatalogServices services,
        [Description("The text string to use when search for related items in the catalog"), Required, MinLength(1)] string text)
    {
        var page = await CatalogGraft.SearchAsync(
            services,
            paginationRequest.PageIndex,
            paginationRequest.PageSize,
            text);
        return TypedResults.Ok(page);
    }

    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public static async Task<Ok<PaginatedItems<CatalogItem>>> GetItemsByBrandAndTypeId(
        [AsParameters] PaginationRequest paginationRequest,
        [AsParameters] CatalogServices services,
        [Description("The type of items to return")] int typeId,
        [Description("The brand of items to return")] int? brandId)
    {
        return await GetAllItems(paginationRequest, services, null, [typeId], brandId is null ? null : [brandId.Value]);
    }

    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public static async Task<Ok<PaginatedItems<CatalogItem>>> GetItemsByBrandId(
        [AsParameters] PaginationRequest paginationRequest,
        [AsParameters] CatalogServices services,
        [Description("The brand of items to return")] int? brandId)
    {
        return await GetAllItems(paginationRequest, services, null, null, brandId is null ? null : [brandId.Value]);
    }

    public static async Task<Results<Created, BadRequest<ProblemDetails>, NotFound<ProblemDetails>>> UpdateItemV1(
        HttpContext httpContext,
        [AsParameters] CatalogServices services,
        CatalogItem productToUpdate)
    {
        if (productToUpdate?.Id == null)
        {
            return TypedResults.BadRequest<ProblemDetails>(new (){
                Detail = "Item id must be provided in the request body."
            });
        }
        return await UpdateItem(httpContext, productToUpdate.Id, services, productToUpdate);
    }

    public static async Task<Results<Created, BadRequest<ProblemDetails>, NotFound<ProblemDetails>>> UpdateItem(
        HttpContext httpContext,
        [Description("The id of the catalog item to delete")] int id,
        [AsParameters] CatalogServices services,
        CatalogItem productToUpdate)
    {
        var result = await CatalogGraft.UpdateItemAsync(services, id, productToUpdate);
        return result.StatusCode switch
        {
            StatusCodes.Status400BadRequest => TypedResults.BadRequest<ProblemDetails>(new() { Detail = result.Detail }),
            StatusCodes.Status404NotFound => TypedResults.NotFound<ProblemDetails>(new() { Detail = result.Detail }),
            _ => TypedResults.Created($"/api/catalog/items/{id}")
        };
    }

    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public static async Task<Created> CreateItem(
        [AsParameters] CatalogServices services,
        CatalogItem product)
    {
        var id = await CatalogGraft.CreateItemAsync(services, product);
        return TypedResults.Created($"/api/catalog/items/{id}");
    }

    public static async Task<Results<NoContent, NotFound>> DeleteItemById(
        [AsParameters] CatalogServices services,
        [Description("The id of the catalog item to delete")] int id)
    {
        var deleted = await CatalogGraft.DeleteItemAsync(services, id);
        return deleted ? TypedResults.NoContent() : TypedResults.NotFound();
    }

    public static string GetFullPath(string contentRootPath, string pictureFileName) =>
        CatalogGraft.GetFullPath(contentRootPath, pictureFileName);
}
