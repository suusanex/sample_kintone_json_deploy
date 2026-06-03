using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using KintoneJsDeploy.Cli.Models;
using KintoneJsDeploy.Cli.Options;

namespace KintoneJsDeploy.Cli.Services;

internal sealed class OAuthService(HttpClient httpClient)
{
    private const string Scope = "k:app_settings:read k:app_settings:write k:file:write";
    private const string AuthorizationEndpointPath = "/oauth2/authorization";
    private readonly HttpClient _httpClient = httpClient;

    public string BuildAuthorizationUrl(GetTokenCommandOptions options, string state)
    {
        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = options.ClientId,
            ["redirect_uri"] = options.RedirectUri.ToString(),
            ["scope"] = Scope,
            ["state"] = state
        };

        return BuildUrl(AuthorizationEndpointPath, options.Subdomain, query);
    }

    public async Task<OAuthTokenResponse> ExchangeAuthorizationCodeForAccessTokenAsync(
        GetTokenCommandOptions options,
        string authorizationCode,
        CancellationToken cancellationToken)
    {
        var formData = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = authorizationCode,
            ["redirect_uri"] = options.RedirectUri.ToString()
        };

        return await RequestTokenAsync(options.Subdomain, options.ClientId, options.ClientSecret, formData, cancellationToken);
    }

    public async Task<OAuthTokenResponse> RefreshAccessTokenAsync(
        string subdomain,
        string clientId,
        string clientSecret,
        string refreshToken,
        CancellationToken cancellationToken)
    {
        var formData = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken
        };

        return await RequestTokenAsync(subdomain, clientId, clientSecret, formData, cancellationToken);
    }

    private async Task<OAuthTokenResponse> RequestTokenAsync(
        string subdomain,
        string clientId,
        string clientSecret,
        Dictionary<string, string> formData,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(subdomain, "/oauth2/token"))
        {
            Content = new FormUrlEncodedContent(formData)
        };

        var basicToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicToken);
        request.Headers.Accept.ParseAdd("application/json");

        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            using var failedResponse = response;
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Token endpoint failed. endpoint=/oauth2/token statusCode={(int)response.StatusCode} response={body}");
        }

        using var responseForDispose = response;
        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var token = await JsonSerializer.DeserializeAsync<OAuthTokenResponse>(responseStream, cancellationToken: cancellationToken)
                    ?? throw new InvalidOperationException("Failed to parse token endpoint response.");

        if (string.IsNullOrWhiteSpace(token.AccessToken))
        {
            throw new InvalidOperationException("Token endpoint response did not contain access_token.");
        }

        return token;
    }

    private static Uri BuildUri(string subdomain, string relativePath)
    {
        return new Uri($"https://{subdomain}.kintone.com{relativePath}");
    }

    private static string BuildUrl(string relativePath, string subdomain, Dictionary<string, string> query)
    {
        var queryText = string.Join(
            "&",
            query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

        return new UriBuilder(BuildUri(subdomain, relativePath)) { Query = queryText }.ToString();
    }
}
