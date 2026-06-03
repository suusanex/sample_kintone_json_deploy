namespace KintoneJsDeploy.Cli.Options;

internal sealed record GetTokenCommandOptions(
    string Subdomain,
    string ClientId,
    string ClientSecret,
    Uri RedirectUri,
    string Scope,
    int TimeoutSeconds)
{
    public static GetTokenCommandOptions FromEnvironment()
    {
        var subdomain = GetRequiredEnvironmentVariable("KINTONE_SUBDOMAIN");
        var clientId = GetRequiredEnvironmentVariable("KINTONE_OAUTH_CLIENT_ID");
        var clientSecret = GetRequiredEnvironmentVariable("KINTONE_OAUTH_CLIENT_SECRET");
        var redirectUriValue = GetRequiredEnvironmentVariable("KINTONE_OAUTH_REDIRECT_URI");
        var scope = GetOptionalEnvironmentVariable("KINTONE_OAUTH_SCOPE") ??
                    "k:app_settings:read k:app_settings:write k:file:write";
        var timeoutSeconds = GetOptionalEnvironmentVariable("KINTONE_OAUTH_TIMEOUT_SECONDS");
        var timeout = string.IsNullOrWhiteSpace(timeoutSeconds) ? 300 : ParsePositiveTimeout(timeoutSeconds);

        if (!Uri.TryCreate(redirectUriValue, UriKind.Absolute, out var redirectUri))
        {
            throw new ArgumentException("KINTONE_OAUTH_REDIRECT_URI must be an absolute URI.");
        }

        if (redirectUri.Scheme != Uri.UriSchemeHttp && redirectUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("KINTONE_OAUTH_REDIRECT_URI must use http or https.");
        }

        return new GetTokenCommandOptions(
            subdomain,
            clientId,
            clientSecret,
            redirectUri,
            scope,
            timeout);
    }

    private static string GetRequiredEnvironmentVariable(string key)
    {
        var value = Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Environment variable '{key}' is required.");
        }

        return value;
    }

    private static string? GetOptionalEnvironmentVariable(string key)
    {
        return Environment.GetEnvironmentVariable(key);
    }

    private static int ParsePositiveTimeout(string value)
    {
        if (!int.TryParse(value, out var timeout) || timeout <= 0)
        {
            throw new ArgumentException("KINTONE_OAUTH_TIMEOUT_SECONDS must be a positive integer.");
        }

        return timeout;
    }
}
