namespace eShop.ClientApp.Services;

// The settings screen still stores one URL per service. Those URLs are graft WebSocket hosts.
// An empty value uses the emulator host (10.0.2.2 on Android, localhost elsewhere) on port 8000.
public static class GraftHosts
{
    public const int DefaultPort = 8000;

    public static string Default()
    {
        var host = DeviceInfo.Platform == DevicePlatform.Android ? "10.0.2.2" : "localhost";
        return $"ws://{host}:{DefaultPort}/ws";
    }

    public static string Socket(string configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return Default();
        }

        var value = configured.Trim().TrimEnd('/');
        if (value.StartsWith("ws://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
        {
            return value.EndsWith("/ws", StringComparison.OrdinalIgnoreCase) ? value : value + "/ws";
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            var scheme = uri.Scheme == Uri.UriSchemeHttps ? "wss" : "ws";
            var port = uri.IsDefaultPort ? string.Empty : ":" + uri.Port;
            return $"{scheme}://{uri.Host}{port}/ws";
        }

        return value;
    }
}
