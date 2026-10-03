// Graftcode basket slice — Redis stays in Grpc/BasketService.
// Storefront basket calls use that type through Graftcode Gateway, not gRPC.
var builder = WebApplication.CreateBuilder(args);

builder.AddBasicServiceDefaults();
builder.AddApplicationServices();

var app = builder.Build();

app.MapDefaultEndpoints();
app.Run();
