using System.Globalization;

namespace KintoneOAuthTokenCli;

public sealed class OAuthCliOptions
{
    public required string Subdomain { get; init; }
    public required string ClientId { get; init; }
    public required string ClientSecret { get; init; }
    public Uri RedirectUri { get; init; } = new("https://localhost:54187/oauth");
    public string Scope { get; init; } = "k:app_record:read";
    public int TimeoutSeconds { get; init; } = 300;

    public static OAuthCliOptions Parse(string[] args)
    {
        string? subdomain = null;
        string? clientId = null;
        string? clientSecret = null;
        Uri? redirectUri = null;
        string? scope = null;
        int timeoutSeconds = 300;

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--subdomain" && i + 1 < args.Length)
            {
                subdomain = args[++i];
            }
            else if (args[i] == "--client-id" && i + 1 < args.Length)
            {
                clientId = args[++i];
            }
            else if (args[i] == "--client-secret" && i + 1 < args.Length)
            {
                clientSecret = args[++i];
            }
            else if (args[i] == "--redirect-uri" && i + 1 < args.Length)
            {
                redirectUri = new Uri(args[++i]);
            }
            else if (args[i] == "--scope" && i + 1 < args.Length)
            {
                scope = args[++i];
            }
            else if (args[i] == "--timeout-seconds" && i + 1 < args.Length)
            {
                timeoutSeconds = int.Parse(args[++i], CultureInfo.InvariantCulture);
            }
        }

        if (string.IsNullOrWhiteSpace(subdomain))
            throw new ArgumentException("--subdomain is required");
        if (string.IsNullOrWhiteSpace(clientId))
            throw new ArgumentException("--client-id is required");
        if (string.IsNullOrWhiteSpace(clientSecret))
            throw new ArgumentException("--client-secret is required");

        var finalRedirectUri = redirectUri ?? new Uri("https://localhost:54187/oauth");
        if (!string.Equals(finalRedirectUri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("--redirect-uri must use https scheme");
        if (!string.Equals(finalRedirectUri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("--redirect-uri host must be localhost");

        return new OAuthCliOptions
        {
            Subdomain = subdomain,
            ClientId = clientId,
            ClientSecret = clientSecret,
            RedirectUri = finalRedirectUri,
            Scope = scope ?? "k:app_record:read",
            TimeoutSeconds = timeoutSeconds
        };
    }
}
