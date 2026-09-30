// Graftcode catalog slice — this process migrates the database for Aspire.
// Storefront reads call CatalogApi through Graftcode Gateway, not HTTP routes.
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddApplicationServices();

var app = builder.Build();

app.MapDefaultEndpoints();
app.Run();
