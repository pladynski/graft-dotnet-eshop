using eShop.HybridApp.Services;
using Microsoft.Extensions.Logging;

namespace eShop.HybridApp;

public static class MauiProgram
{
    // Emulator host for the catalog graft. Android cannot reach the host machine as localhost.
    internal static string CatalogGraftHost = DeviceInfo.Platform == DevicePlatform.Android ? "ws://10.0.2.2:8000/ws" : "ws://localhost:8000/ws";


    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif


        // The shared catalog graft reads CATALOG_GRAFT_HOST in its static constructor.
        Environment.SetEnvironmentVariable("CATALOG_GRAFT_HOST", CatalogGraftHost);
        builder.Services.AddSingleton<eShop.WebAppComponents.Services.CatalogService>();
        builder.Services.AddSingleton<CatalogService>();
        builder.Services.AddSingleton<eShop.WebAppComponents.Services.ICatalogService>(sp => sp.GetRequiredService<CatalogService>());
        builder.Services.AddSingleton<eShop.WebAppComponents.Services.IProductImageUrlProvider, ProductImageUrlProvider>();

        return builder.Build();
    }
}
