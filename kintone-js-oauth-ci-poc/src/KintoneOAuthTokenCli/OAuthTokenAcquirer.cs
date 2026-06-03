using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KintoneOAuthTokenCli;

public sealed class OAuthTokenAcquirer
{
    private readonly HttpClient _httpClient;

    public OAuthTokenAcquirer(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public string BuildAuthorizationUri(OAuthCliOptions options, string state)
    {
        var tenantHost = ResolveTenantHost(options.Subdomain);
        // スコープにはスペースが含まれる場合がある（複数スコープをスペース区切り）
        // Kintone OAuthではスペースを + でエンコードする必要がある
        var encodedScope = System.Net.WebUtility.UrlEncode(options.Scope);
        return $"https://{tenantHost}/oauth2/authorization" +
               $"?response_type=code" +
               $"&client_id={Uri.EscapeDataString(options.ClientId)}" +
               $"&redirect_uri={Uri.EscapeDataString(options.RedirectUri.ToString())}" +
               $"&scope={encodedScope}" +
               $"&state={Uri.EscapeDataString(state)}";
    }

    public static string ExtractAccessToken(string responseBody)
    {
        using var doc = JsonDocument.Parse(responseBody);
        if (!doc.RootElement.TryGetProperty("access_token", out var tokenElement))
            throw new InvalidOperationException($"access_token not found in token response: {responseBody}");

        var token = tokenElement.GetString();
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("access_token is empty in token response");

        return token;
    }

    public async Task<string> AcquireTokenAsync(OAuthCliOptions options, CancellationToken cancellationToken)
    {
        // CSRF対策のためのランダムなstate値を生成
        var state = Guid.NewGuid().ToString("N");
        var authorizationUri = BuildAuthorizationUri(options, state);
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        Console.Error.WriteLine($"[OAuthCli] Authorization URL: {authorizationUri}");
        Console.Error.WriteLine($"[OAuthCli] Callback listener: https://localhost:{options.RedirectUri.Port}{options.RedirectUri.AbsolutePath}");

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        
        // Kestrel を使用して HTTPS を構成（開発証明書を使用）
        builder.WebHost.UseKestrel(kestrelOptions =>
        {
            kestrelOptions.ListenLocalhost(options.RedirectUri.Port, listenOptions =>
            {
                try
                {
                    // ASP.NET Core 開発証明書を使用
                    listenOptions.UseHttps();
                    Console.Error.WriteLine($"[OAuthCli] HTTPS listener configured on port {options.RedirectUri.Port}");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[OAuthCli] Warning: Failed to configure HTTPS: {ex.Message}");
                    Console.Error.WriteLine($"[OAuthCli] Hint: Run 'dotnet dev-certs https --trust' to trust the development certificate");
                    throw;
                }
            });
        });
        
        var app = builder.Build();

        app.MapGet(options.RedirectUri.AbsolutePath, async (HttpContext context) =>
        {
            var query = context.Request.QueryString.Value ?? string.Empty;
            Console.Error.WriteLine($"[OAuthCli] Callback received. Query: {query}");
            var code = ExtractQueryParameter(query, "code");
            var error = ExtractQueryParameter(query, "error");
            var receivedState = ExtractQueryParameter(query, "state");

            if (!string.IsNullOrEmpty(error))
            {
                var errorDescription = ExtractQueryParameter(query, "error_description");
                tcs.TrySetException(new InvalidOperationException($"OAuth error: {error} - {errorDescription}"));
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("OAuth authorization failed.");
                return;
            }

            // state パラメータの検証（CSRF対策）
            if (receivedState != state)
            {
                Console.Error.WriteLine($"[OAuthCli] State mismatch. Expected: {state}, Received: {receivedState}");
                tcs.TrySetException(new InvalidOperationException($"State parameter mismatch. Possible CSRF attack."));
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("State validation failed.");
                return;
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                tcs.TrySetException(new InvalidOperationException("Authorization code not received."));
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("Authorization code was not found.");
                return;
            }

            tcs.TrySetResult(code);
            context.Response.StatusCode = 200;
            await context.Response.WriteAsync("Authorization completed. You can close this window.");
        });

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

        Console.Error.WriteLine("[OAuthCli] Starting HTTPS callback listener...");
        var serverTask = app.RunAsync(timeoutCts.Token);
        
        try
        {
            // サーバーが起動するまで少し待機
            await Task.Delay(500, timeoutCts.Token);
            Console.Error.WriteLine("[OAuthCli] Callback listener started successfully");
            
            Console.Error.WriteLine("[OAuthCli] Opening browser for OAuth authorization...");
            OpenBrowser(authorizationUri);
            
            var authCode = await tcs.Task.WaitAsync(timeoutCts.Token);
            Console.Error.WriteLine("[OAuthCli] Authorization code received. Exchanging token...");
            return await ExchangeCodeForTokenAsync(options, authCode, timeoutCts.Token);
        }
        catch (OperationCanceledException ex) when (timeoutCts.IsCancellationRequested)
        {
            throw new TimeoutException($"OAuth flow timed out after {options.TimeoutSeconds} seconds.", ex);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[OAuthCli] Error during OAuth flow: {ex.GetType().Name}: {ex.Message}");
            throw;
        }
        finally
        {
            Console.Error.WriteLine("[OAuthCli] Stopping callback listener...");
            await app.StopAsync(CancellationToken.None);
            try
            {
                await serverTask;
            }
            catch (OperationCanceledException)
            {
                // タイムアウト終了時のキャンセルは想定内
            }
        }
    }

    private async Task<string> ExchangeCodeForTokenAsync(OAuthCliOptions options, string authorizationCode, CancellationToken cancellationToken)
    {
        var tenantHost = ResolveTenantHost(options.Subdomain);
        var tokenEndpoint = $"https://{tenantHost}/oauth2/token";
        Console.Error.WriteLine($"[OAuthCli] Token endpoint: {tokenEndpoint}");
        using var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new("code", authorizationCode),
            new("redirect_uri", options.RedirectUri.ToString()),
            new("client_id", options.ClientId),
            new("client_secret", options.ClientSecret)
        });

        using var response = await _httpClient.PostAsync(tokenEndpoint, content, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        Console.Error.WriteLine($"[OAuthCli] Token endpoint response: {(int)response.StatusCode} {response.StatusCode}");

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Token exchange failed. Status={(int)response.StatusCode}, Body={responseBody}");

        return ExtractAccessToken(responseBody);
    }

    private static string ResolveTenantHost(string subdomainOrHost)
    {
        if (string.IsNullOrWhiteSpace(subdomainOrHost))
            throw new ArgumentException("subdomainOrHost is required", nameof(subdomainOrHost));

        if (subdomainOrHost.Contains('.', StringComparison.Ordinal))
            return subdomainOrHost;

        return $"{subdomainOrHost}.kintone.com";
    }

    private static void OpenBrowser(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = uri, UseShellExecute = true });
        }
        catch
        {
            Console.Error.WriteLine("Browser auto-launch failed. Open the following URL manually:");
            Console.Error.WriteLine(uri);
        }
    }

    public static string? ExtractQueryParameter(string query, string key)
    {
        if (string.IsNullOrWhiteSpace(query))
            return null;

        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0] == key)
                return Uri.UnescapeDataString(parts[1]);
        }

        return null;
    }
}
