namespace KintoneJsDeploy.Cli.Options;

internal sealed record DeployCommandOptions(
    string Subdomain,
    int AppId,
    string ClientId,
    string ClientSecret,
    string RefreshToken)
{
    public static DeployCommandOptions FromEnvironment()
    {
        var subdomain = GetRequiredEnvironmentVariable("KINTONE_SUBDOMAIN");
        var appIdValue = GetRequiredEnvironmentVariable("KINTONE_APP_ID");
        var clientId = GetRequiredEnvironmentVariable("KINTONE_OAUTH_CLIENT_ID");
        var clientSecret = GetRequiredEnvironmentVariable("KINTONE_OAUTH_CLIENT_SECRET");
        var refreshToken = GetRequiredEnvironmentVariable("KINTONE_REFRESH_TOKEN");

        if (!int.TryParse(appIdValue, out var appId))
        {
            throw new ArgumentException("Environment variable 'KINTONE_APP_ID' must be an integer.");
        }

        return new DeployCommandOptions(subdomain, appId, clientId, clientSecret, refreshToken);
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
