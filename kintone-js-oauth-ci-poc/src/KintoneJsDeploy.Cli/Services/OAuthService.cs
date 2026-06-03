using System.Net.Http.Headers;
using System.Net;
using System.Text;
using System.Text.Json;
using KintoneJsDeploy.Cli.Models;
using KintoneJsDeploy.Cli.Options;

namespace KintoneJsDeploy.Cli.Services;

internal sealed class OAuthService(HttpClient httpClient)
{
    private const string AuthorizationEndpointPath = "/oauth2/authorization";
    private readonly HttpClient _httpClient = httpClient;

    public string BuildAuthorizationUrl(GetTokenCommandOptions options, string state)
    {
        var tenantHost = ResolveTenantHost(options.Subdomain);
        var encodedScope = WebUtility.UrlEncode(options.Scope);

        return $"https://{tenantHost}{AuthorizationEndpointPath}" +
               $"?response_type=code" +
               $"&client_id={Uri.EscapeDataString(options.ClientId)}" +
               $"&redirect_uri={Uri.EscapeDataString(options.RedirectUri.ToString())}" +
               $"&scope={encodedScope}" +
               $"&state={Uri.EscapeDataString(state)}";
    }

    public string BuildTokenEndpointUri(string subdomain)
    {
        var tenantHost = ResolveTenantHost(subdomain);
        return $"https://{tenantHost}/oauth2/token";
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
        var request = new HttpRequestMessage(HttpMethod.Post, BuildTokenEndpointUri(subdomain))
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

    private static string ResolveTenantHost(string subdomainOrHost)
    {
        if (string.IsNullOrWhiteSpace(subdomainOrHost))
        {
            throw new ArgumentException("Subdomain or host is required.", nameof(subdomainOrHost));
        }

        if (subdomainOrHost.Contains('.', StringComparison.Ordinal))
        {
            return subdomainOrHost;
        }

        return $"{subdomainOrHost}.kintone.com";
    }
}
