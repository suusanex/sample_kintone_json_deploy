namespace KintoneJsDeploy.Cli.Options;

internal sealed record GetTokenCommandOptions(
    string Subdomain,
    string ClientId,
    string ClientSecret,
    Uri RedirectUri)
{
    public static GetTokenCommandOptions FromEnvironment()
    {
        var subdomain = GetRequiredEnvironmentVariable("KINTONE_SUBDOMAIN");
        var clientId = GetRequiredEnvironmentVariable("KINTONE_OAUTH_CLIENT_ID");
        var clientSecret = GetRequiredEnvironmentVariable("KINTONE_OAUTH_CLIENT_SECRET");
        var redirectUriValue = GetRequiredEnvironmentVariable("KINTONE_OAUTH_REDIRECT_URI");

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
            redirectUri);
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
}
