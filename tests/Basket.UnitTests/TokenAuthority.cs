using System.Linq;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace eShop.Basket.UnitTests;

internal sealed class TokenAuthority : IAsyncDisposable
{
    private readonly WebApplication app;
    private readonly RsaSecurityKey key;
    private readonly object signGate = new();

    private TokenAuthority(WebApplication app, RsaSecurityKey key, string issuer)
    {
        this.app = app;
        this.key = key;
        Issuer = issuer;
    }

    public string Issuer { get; }

    public const string Audience = "basket";

    public static async Task<TokenAuthority> StartAsync()
    {
        var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "basket-test" };
        var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(key);
        jwk.Use = "sig";
        jwk.Alg = SecurityAlgorithms.RsaSha256;
        jwk.KeyId = key.KeyId;
        var issuer = new string[1];

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = Array.Empty<string>() });
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string>
        {
            ["Kestrel:EndpointDefaults:Protocols"] = "Http1"
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.MapGet("/.well-known/openid-configuration", () =>
        {
            var current = issuer[0];
            return Results.Json(new
            {
                issuer = current,
                jwks_uri = current + "/.well-known/openid-configuration/jwks",
                authorization_endpoint = current + "/connect/authorize",
                token_endpoint = current + "/connect/token",
                id_token_signing_alg_values_supported = new[] { "RS256" },
                response_types_supported = new[] { "code" },
                subject_types_supported = new[] { "public" }
            });
        });
        app.MapGet("/.well-known/openid-configuration/jwks", () => Results.Json(new { keys = new[] { jwk } }));
        await app.StartAsync();
        issuer[0] = app.Urls.Single().TrimEnd('/');
        return new TokenAuthority(app, key, issuer[0]);
    }

    public string Issue(string subject, string scope, DateTime? expires = null, string audience = null, bool otherKey = false)
    {
        SigningCredentials credentials;
        if (otherKey)
        {
            var other = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "other" };
            credentials = new SigningCredentials(other, SecurityAlgorithms.RsaSha256);
        }
        else
        {
            credentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience ?? Audience,
            NotBefore = DateTime.UtcNow.AddMinutes(-5),
            Expires = expires ?? DateTime.UtcNow.AddMinutes(10),
            IssuedAt = DateTime.UtcNow.AddMinutes(-1),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = subject ?? string.Empty,
                ["scope"] = scope ?? string.Empty
            },
            SigningCredentials = credentials
        };

        lock (signGate)
        {
            return new JsonWebTokenHandler().CreateToken(descriptor);
        }
    }

    public ValueTask DisposeAsync() => app.DisposeAsync();
}
