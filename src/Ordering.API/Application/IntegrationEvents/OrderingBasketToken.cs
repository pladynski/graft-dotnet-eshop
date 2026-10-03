using System.Text.Json;

namespace eShop.Ordering.API.Application.IntegrationEvents;

// Client-credentials token for OnOrderStarted. Scope basket.internal is not granted to webapp or maui.
internal static class OrderingBasketToken
{
    private const string Scope = "basket.internal";
    private static readonly object Gate = new();
    private static readonly HttpClient Http = new();
    private static string cached;
    private static DateTimeOffset refreshAfter;

    public static string Get()
    {
        lock (Gate)
        {
            if (!string.IsNullOrEmpty(cached) && DateTimeOffset.UtcNow < refreshAfter)
            {
                return cached;
            }
        }

        var fresh = Request();
        lock (Gate)
        {
            cached = fresh.Token;
            refreshAfter = fresh.RefreshAfter;
            return cached;
        }
    }

    private static (string Token, DateTimeOffset RefreshAfter) Request()
    {
        var identityUrl = Environment.GetEnvironmentVariable("Identity__Url");
        if (string.IsNullOrWhiteSpace(identityUrl))
        {
            throw new InvalidOperationException("Ordering requires Identity__Url to call the basket graft.");
        }

        var clientId = Environment.GetEnvironmentVariable("Ordering__BasketClientId");
        if (string.IsNullOrWhiteSpace(clientId))
        {
            clientId = "ordering";
        }

        var clientSecret = Environment.GetEnvironmentVariable("Ordering__BasketClientSecret");
        if (string.IsNullOrWhiteSpace(clientSecret))
        {
            clientSecret = "secret";
        }

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["scope"] = Scope
        });

        using var response = Http.PostAsync(identityUrl.TrimEnd('/') + "/connect/token", content).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                "Ordering could not obtain a basket service token (" + (int)response.StatusCode + ").");
        }

        using var stream = response.Content.ReadAsStream();
        using var document = JsonDocument.Parse(stream);
        if (!document.RootElement.TryGetProperty("access_token", out var accessToken)
            || accessToken.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException("Ordering basket token response did not include an access token.");
        }

        var token = accessToken.GetString();
        if (string.IsNullOrEmpty(token))
        {
            throw new InvalidOperationException("Ordering basket token response did not include an access token.");
        }

        var expiresIn = 60;
        if (document.RootElement.TryGetProperty("expires_in", out var expires) && expires.TryGetInt32(out var seconds) && seconds > 0)
        {
            expiresIn = seconds;
        }

        var refresh = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, expiresIn - 30));
        return (token, refresh);
    }
}
