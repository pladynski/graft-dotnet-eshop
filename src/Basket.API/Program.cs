// Graftcode basket slice — Redis and the order-started consumer stay here.
// Storefront basket calls use BasketService through Graftcode Gateway, not gRPC.
var builder = WebApplication.CreateBuilder(args);

builder.AddBasicServiceDefaults();
builder.AddApplicationServices();

var app = builder.Build();

app.MapDefaultEndpoints();
app.Run();
