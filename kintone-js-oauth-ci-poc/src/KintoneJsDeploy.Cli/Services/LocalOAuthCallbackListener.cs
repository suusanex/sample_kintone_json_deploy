using System.Net;
using System.Text;

namespace KintoneJsDeploy.Cli.Services;

internal sealed class LocalOAuthCallbackListener : IDisposable
{
    private readonly HttpListener _listener;
    private readonly string _expectedPath;

    public LocalOAuthCallbackListener(Uri redirectUri)
    {
        var path = redirectUri.AbsolutePath;
        _expectedPath = string.IsNullOrEmpty(path) || path == "/" ? "/" : path.TrimEnd('/');
        var listenerPath = string.IsNullOrEmpty(path) || path == "/" ? "/" : path.TrimEnd('/') + "/";

        _listener = new HttpListener();
        _listener.Prefixes.Add($"{redirectUri.Scheme}://{redirectUri.Authority}{listenerPath}");
        if (listenerPath != "/")
        {
            _listener.Prefixes.Add($"{redirectUri.Scheme}://{redirectUri.Authority}/");
        }
    }

    public async Task<string> WaitForCodeAsync(string expectedState, TimeSpan timeout, CancellationToken cancellationToken)
    {
        _listener.Start();
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            var delayTask = Task.Delay(Timeout.InfiniteTimeSpan, linked.Token);
            while (true)
            {
                var contextTask = _listener.GetContextAsync();
                var completed = await Task.WhenAny(contextTask, delayTask);
                if (completed != contextTask)
                {
                    throw new TimeoutException("OAuth callback was not received within timeout.");
                }

                var context = await contextTask;
                var requestPath = context.Request.Url?.AbsolutePath ?? string.Empty;
                if (!IsCallbackPath(requestPath))
                {
                    await WriteResponseAsync(
                        context,
                        "OAuth Skipped",
                        "This endpoint is not the OAuth callback endpoint.");
                    continue;
                }

                var query = ParseQuery(context.Request.Url?.Query);
                var queryCode = query.GetValueOrDefault("code");
                var queryState = query.GetValueOrDefault("state");
                var error = query.GetValueOrDefault("error");
                var errorDescription = query.GetValueOrDefault("error_description");

                if (!string.IsNullOrEmpty(error))
                {
                    await WriteResponseAsync(
                        context,
                        "OAuth Failed",
                        $"OAuth authorization error: {error}. {errorDescription ?? string.Empty}".Trim());
                    throw new InvalidOperationException(
                        $"OAuth error response from callback: {error} {errorDescription}");
                }

                if (string.IsNullOrWhiteSpace(queryCode))
                {
                    await WriteResponseAsync(context, "OAuth Failed", "code not found in callback.");
                    throw new InvalidOperationException("OAuth callback did not include code.");
                }

                if (!string.Equals(queryState, expectedState, StringComparison.Ordinal))
                {
                    await WriteResponseAsync(context, "OAuth Failed", "state verification failed.");
                    throw new InvalidOperationException("OAuth callback state does not match expected state.");
                }

                await WriteResponseAsync(context, "OAuth Succeeded", "Authorization completed. You can close this window.");
                return queryCode;
            }
        }
        finally
        {
            _listener.Stop();
        }
    }

    public void Dispose()
    {
        _listener.Close();
    }

    private static async Task WriteResponseAsync(HttpListenerContext context, string title, string message)
    {
        var response = context.Response;
        response.StatusCode = 200;
        response.ContentType = "text/html; charset=utf-8";
        var html = $"""
                    <!doctype html>
                    <html lang="en">
                      <head><title>{title}</title></head>
                      <body><h1>{title}</h1><p>{message}</p></body>
                    </html>
                    """;

        var bytes = Encoding.UTF8.GetBytes(html);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        response.Close();
    }

    private bool IsCallbackPath(string requestPath)
    {
        if (string.IsNullOrWhiteSpace(requestPath))
        {
            return false;
        }

        var normalizedRequestPath = requestPath.EndsWith('/')
            ? requestPath.TrimEnd('/')
            : requestPath;
        if (string.IsNullOrEmpty(normalizedRequestPath))
        {
            normalizedRequestPath = "/";
        }

        return string.Equals(normalizedRequestPath, _expectedPath, StringComparison.Ordinal);
    }

    private static Dictionary<string, string> ParseQuery(string? rawQuery)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(rawQuery))
        {
            return result;
        }

        if (rawQuery.StartsWith("?"))
        {
            rawQuery = rawQuery[1..];
        }

        var pairs = rawQuery.Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in pairs)
        {
            var index = pair.IndexOf('=');
            if (index < 0)
            {
                result[Uri.UnescapeDataString(pair)] = string.Empty;
                continue;
            }

            var key = Uri.UnescapeDataString(pair.Substring(0, index));
            var value = Uri.UnescapeDataString(pair[(index + 1)..]);
            result[key] = value;
        }

        return result;
    }
}
