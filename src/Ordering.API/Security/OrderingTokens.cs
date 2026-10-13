// Graft calls do not run JWT bearer middleware. AddDefaultAuthentication is skipped when
// EshopGraftHost is set, and even a registered JwtBearer handler would not see a graft invocation.
// OrderingTokens.ValidateAsync is the check that runs on the graft path, before the database is touched.
using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace eShop.Ordering.API;

internal readonly struct OrderingCaller
{
    private OrderingCaller(string subject, string failure)
    {
        Subject = subject;
        Failure = failure;
    }

    public string Subject { get; }

    public string Failure { get; }

    public bool IsUser => Failure is null && !string.IsNullOrEmpty(Subject);

    public static OrderingCaller User(string subject) => new(subject, null);

    public static OrderingCaller Rejected(string failure) => new(null, failure ?? "unauthenticated");
}

internal static class OrderingTokens
{
    public const string UserScope = "orders";

    private static readonly JsonWebTokenHandler Handler = CreateHandler();
    private static readonly ConcurrentDictionary<string, ConfigurationManager<OpenIdConnectConfiguration>> Metadata = new(StringComparer.OrdinalIgnoreCase);

    public static async Task<OrderingCaller> ValidateAsync(IConfiguration configuration, string authorization)
    {
        if (string.IsNullOrWhiteSpace(authorization))
        {
            return OrderingCaller.Rejected("unauthenticated");
        }

        const string prefix = "Bearer ";
        if (!authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return OrderingCaller.Rejected("unauthenticated");
        }

        var token = authorization.Substring(prefix.Length).Trim();
        if (token.Length == 0)
        {
            return OrderingCaller.Rejected("unauthenticated");
        }

        var identityUrl = configuration["Identity:Url"];
        var audience = configuration["Identity:Audience"];
        if (string.IsNullOrWhiteSpace(identityUrl) || string.IsNullOrWhiteSpace(audience))
        {
            return OrderingCaller.Rejected("unauthenticated");
        }

        OpenIdConnectConfiguration oidc;
        try
        {
            oidc = await MetadataFor(identityUrl).GetConfigurationAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return OrderingCaller.Rejected("unauthenticated");
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
            return OrderingCaller.Rejected("unauthenticated");
        }

        var scopes = ReadScopes(result.ClaimsIdentity);
        if (!scopes.Contains(UserScope))
        {
            return OrderingCaller.Rejected("unauthenticated");
        }

        var subject = result.ClaimsIdentity.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(subject))
        {
            return OrderingCaller.Rejected("unauthenticated");
        }

        return OrderingCaller.User(subject);
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
