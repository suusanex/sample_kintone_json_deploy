using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace KintoneJsDeploy.Cli.Services;

internal sealed class LocalOAuthCallbackListener : IDisposable
{
    private readonly Uri _redirectUri;
    private readonly string _expectedPath;
    private readonly string _expectedSlashPath;

    public LocalOAuthCallbackListener(Uri redirectUri)
    {
        _redirectUri = redirectUri;
        var path = string.IsNullOrWhiteSpace(redirectUri.AbsolutePath) ? "/" : redirectUri.AbsolutePath;
        _expectedPath = path == "/" ? "/" : path.TrimEnd('/');
        _expectedSlashPath = _expectedPath == "/" ? "/" : $"{_expectedPath}/";
    }

    public async Task<string> WaitForCodeAsync(string expectedState, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();

        builder.WebHost.UseKestrel(kestrelOptions =>
        {
            kestrelOptions.ListenLocalhost(_redirectUri.Port, listenOptions =>
            {
                if (_redirectUri.Scheme == Uri.UriSchemeHttps)
                {
                    try
                    {
                        listenOptions.UseHttps();
                        Console.Error.WriteLine($"[OAuth] HTTPS callback listener configured on port {_redirectUri.Port}.");
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(
                            "Failed to configure HTTPS callback listener. Run `dotnet dev-certs https --trust` if the development certificate is not trusted.",
                            ex);
                    }
                }
            });
        });

        using var app = builder.Build();
        RegisterCallbackEndpoint(app, tcs, expectedState);
        var runTask = app.RunAsync(linked.Token);
        Console.Error.WriteLine($"[OAuth] Callback listener started on {_redirectUri.Scheme}://localhost:{_redirectUri.Port}{_expectedPath}");

        try
        {
            await Task.Delay(300, linked.Token);

            var completedTask = await Task.WhenAny(tcs.Task, runTask);

            if (completedTask == tcs.Task)
            {
                return await tcs.Task;
            }

            if (runTask.IsFaulted)
            {
                await runTask;
            }

            if (linked.Token.IsCancellationRequested)
            {
                throw new TimeoutException($"OAuth callback was not received within timeout ({timeout.TotalSeconds:F0}s).");
            }

            throw new OperationCanceledException("OAuth callback listener was canceled.");
        }
        finally
        {
            try
            {
                await app.StopAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[OAuth] Failed to stop callback listener: {ex}");
            }

            try
            {
                await runTask;
            }
            catch (OperationCanceledException)
            {
                // expected when listener is stopped by cancellation
            }
        }
    }

    public void Dispose()
    {
        // no persistent resources
    }

    private void RegisterCallbackEndpoint(
        WebApplication app,
        TaskCompletionSource<string> tcs,
        string expectedState)
    {
        async Task WriteResponse(HttpContext context, int statusCode, string message)
        {
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync($"""
                    <!doctype html>
                    <html lang="en">
                      <head><title>OAuth</title></head>
                      <body><h1>OAuth</h1><p>{message}</p></body>
                    </html>
                    """);
        }

        async Task Handler(HttpContext context)
        {
            try
            {
                Console.Error.WriteLine($"[OAuth] Callback received path={context.Request.Path} query={context.Request.QueryString}");
                var query = context.Request.Query;
                var error = query["error"].ToString();
                var errorDescription = query["error_description"].ToString();
                var state = query["state"].ToString();
                var code = query["code"].ToString();

                if (!string.IsNullOrEmpty(error))
                {
                    var detail = string.IsNullOrWhiteSpace(errorDescription)
                        ? error
                        : $"{error}: {errorDescription}";
                    tcs.TrySetException(new InvalidOperationException($"OAuth authorization error: {detail}"));
                    await WriteResponse(context, 400, detail);
                    return;
                }

                if (!string.Equals(state, expectedState, StringComparison.Ordinal))
                {
                    tcs.TrySetException(new InvalidOperationException("OAuth callback state does not match expected value."));
                    await WriteResponse(context, 400, "OAuth state verification failed.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(code))
                {
                    tcs.TrySetException(new InvalidOperationException("OAuth callback did not include authorization code."));
                    await WriteResponse(context, 400, "OAuth code was not found.");
                    return;
                }

                if (!tcs.TrySetResult(code))
                {
                    Console.Error.WriteLine("[OAuth] Callback code was already handled. Ignore duplicate request.");
                }
                await WriteResponse(context, 200, "Authorization completed. You can close this window.");
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
                try
                {
                    await WriteResponse(context, 500, $"OAuth callback failed: {ex.Message}");
                }
                catch
                {
                    // ignore write failures on callback failure
                }
            }
        }

        app.MapGet(_expectedPath, Handler);
        if (_expectedSlashPath != "/")
        {
            app.MapGet(_expectedSlashPath, Handler);
        }

        app.MapGet("/", static context => context.Response.WriteAsync("OAuth callback is waiting."));
    }

}
