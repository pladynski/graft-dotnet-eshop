// Graftcode catalog slice — HTTP host kept; storefront reads CatalogGraft via Gateway.
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddApplicationServices();
builder.Services.AddProblemDetails();

var withApiVersioning = builder.Services.AddApiVersioning(options =>
{
    // Include "api-supported-versions" and "api-deprecated-versions" headers in all responses
    options.ReportApiVersions = true;
});

builder.AddDefaultOpenApi(withApiVersioning);

var app = builder.Build();

app.MapDefaultEndpoints();

app.UseStatusCodePages();

// Legacy HTTP routes stay for functional tests, the picture proxy, and the rest of Aspire.
// The storefront catalog client calls CatalogGraft through Graftcode Gateway instead.
app.MapCatalogApi();

app.UseDefaultOpenApi();
app.Run();
