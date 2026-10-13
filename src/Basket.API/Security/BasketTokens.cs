// Graft calls do not run JWT bearer middleware. AddDefaultAuthentication is skipped when
// EshopGraftHost is set, and even a registered JwtBearer handler would not see a graft invocation.
// BasketTokens.ValidateAsync is the check that runs on the graft path, before Redis is touched.
using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace eShop.Basket.API;

internal readonly struct BasketCaller
{
    private BasketCaller(string subject, string failure, bool service)
    {
        Subject = subject;
        Failure = failure;
        IsService = service;
    }

    public string Subject { get; }

    public string Failure { get; }

    public bool IsService { get; }

    public bool IsUser => !IsService && Failure is null && !string.IsNullOrEmpty(Subject);

    public bool IsAnonymous => !IsService && Failure is null && string.IsNullOrEmpty(Subject);

    public static BasketCaller Anonymous() => new(null, null, false);

    public static BasketCaller User(string subject) => new(subject, null, false);

    public static BasketCaller Service() => new(null, null, true);

    public static BasketCaller Rejected(string failure) => new(null, failure, false);
}

internal static class BasketTokens
{
    public const string UserScope = "basket";
    public const string ServiceScope = "basket.internal";

    private static readonly JsonWebTokenHandler Handler = CreateHandler();
    private static readonly ConcurrentDictionary<string, ConfigurationManager<OpenIdConnectConfiguration>> Metadata = new(StringComparer.OrdinalIgnoreCase);

    public static async Task<BasketCaller> ValidateAsync(IConfiguration configuration, string authorization, bool serviceOperation)
    {
        if (string.IsNullOrWhiteSpace(authorization))
        {
            return serviceOperation ? BasketCaller.Rejected("unauthenticated") : BasketCaller.Anonymous();
        }

        const string prefix = "Bearer ";
        if (!authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return BasketCaller.Rejected("unauthenticated");
        }

        var token = authorization.Substring(prefix.Length).Trim();
        if (token.Length == 0)
        {
            return BasketCaller.Rejected("unauthenticated");
        }

        var identityUrl = configuration["Identity:Url"];
        var audience = configuration["Identity:Audience"];
        if (string.IsNullOrWhiteSpace(identityUrl) || string.IsNullOrWhiteSpace(audience))
        {
            return BasketCaller.Rejected("unauthenticated");
        }

        OpenIdConnectConfiguration oidc;
        try
        {
            oidc = await MetadataFor(identityUrl).GetConfigurationAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return BasketCaller.Rejected("unauthenticated");
        }

        var issuers = new HashSet<string>(StringComparer.Ordinal)
        {
            identityUrl.TrimEnd('/'),
            identityUrl.TrimEnd('/') + "/",
            oidc.Issuer
        };
        issuers.Remove(null);
        issuers.Remove(string.Empty);

        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuers = issuers,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = oidc.SigningKeys,
            ClockSkew = TimeSpan.FromMinutes(2)
        };

        var result = await Handler.ValidateTokenAsync(token, parameters).ConfigureAwait(false);
        if (!result.IsValid || result.ClaimsIdentity is null)
        {
            return BasketCaller.Rejected("unauthenticated");
        }

        var scopes = ReadScopes(result.ClaimsIdentity);
        if (serviceOperation)
        {
            return scopes.Contains(ServiceScope)
                ? BasketCaller.Service()
                : BasketCaller.Rejected("forbidden");
        }

        if (!scopes.Contains(UserScope))
        {
            return BasketCaller.Rejected("unauthenticated");
        }

        var subject = result.ClaimsIdentity.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(subject))
        {
            return BasketCaller.Rejected("unauthenticated");
        }

        return BasketCaller.User(subject);
    }

    private static JsonWebTokenHandler CreateHandler()
    {
        JsonWebTokenHandler.DefaultInboundClaimTypeMap.Remove("sub");
        return new JsonWebTokenHandler();
    }

    private static ConfigurationManager<OpenIdConnectConfiguration> MetadataFor(string identityUrl)
    {
        var authority = identityUrl.TrimEnd('/');
        return Metadata.GetOrAdd(authority, static url =>
        {
            var address = url + "/.well-known/openid-configuration";
            var retriever = new HttpDocumentRetriever { RequireHttps = false };
            return new ConfigurationManager<OpenIdConnectConfiguration>(
                address,
                new OpenIdConnectConfigurationRetriever(),
                retriever);
        });
    }

    private static HashSet<string> ReadScopes(ClaimsIdentity identity)
    {
        var scopes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var claim in identity.Claims)
        {
            if (!string.Equals(claim.Type, "scope", StringComparison.Ordinal)
                && !claim.Type.EndsWith("/scope", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var part in claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                scopes.Add(part);
            }
        }

        return scopes;
    }
}
