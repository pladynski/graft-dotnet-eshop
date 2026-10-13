// Graftcode catalog slice — product images are CatalogApi.GetItemPicture bytes.
using eShop.WebApp.Components;
using eShop.ServiceDefaults;
using eShop.WebAppComponents.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

builder.AddApplicationServices();

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseAntiforgery();

app.UseHttpsRedirection();

app.UseStaticFiles();

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

// Browser <img> tags need a URL. Bytes come from CatalogApi.GetItemPicture over the gateway.
app.MapGet("/product-images/{id:int}", (int id, CatalogService catalog) =>
{
    var picture = catalog.GetItemPicture(id);
    return picture is null
        ? Results.NotFound()
        : Results.File(picture.Value.Bytes, picture.Value.Mime);
});

app.Run();
