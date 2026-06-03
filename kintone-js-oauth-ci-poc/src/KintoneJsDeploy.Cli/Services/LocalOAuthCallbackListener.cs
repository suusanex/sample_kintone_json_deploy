using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace KintoneJsDeploy.Cli.Services;

internal sealed class LocalOAuthCallbackListener : IDisposable
{
    private readonly Uri _redirectUri;
    private readonly string _expectedPath;

    public LocalOAuthCallbackListener(Uri redirectUri)
    {
        _redirectUri = redirectUri;
        var path = string.IsNullOrWhiteSpace(redirectUri.AbsolutePath) ? "/" : redirectUri.AbsolutePath;
        _expectedPath = path == "/" ? "/" : path.TrimEnd('/');
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
                        WriteOAuthLog($"HTTPS callback listener configured on port {_redirectUri.Port}.");
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
        RegisterRequestDiagnostics(app);
        RegisterCallbackEndpoint(app, tcs, expectedState);
        var runTask = app.RunAsync(linked.Token);
        WriteOAuthLog($"Callback listener started on {_redirectUri.Scheme}://localhost:{_redirectUri.Port}{_expectedPath}");
        WriteOAuthLog(
            "Callback diagnostics: if the browser shows an error before a 'Callback received' line appears, " +
            "the failure happened before kintone redirected to the local listener. Check the kintone OAuth client, callback URL, scope, and authorization page error.");

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
                WriteOAuthLog(
                    $"OAuth callback was not received within timeout ({timeout.TotalSeconds:F0}s). " +
                    "No request reached the local callback endpoint. If the browser showed HTTP ERROR 500 without a 'Callback received' log, inspect kintone OAuth settings.");
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
                WriteOAuthLog("Failed to stop callback listener: " + ex.ToString());
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
                WriteOAuthLog($"Callback received path={context.Request.Path} rawQueryLength={context.Request.QueryString.Value?.Length ?? 0}");
                var query = ReadQueryWithDiagnostics(context);
                var error = query["error"].ToString();
                var errorDescription = query["error_description"].ToString();
                var state = query["state"].ToString();
                var code = query["code"].ToString();

                if (!string.IsNullOrEmpty(error))
                {
                    var detail = string.IsNullOrWhiteSpace(errorDescription)
                        ? error
                        : $"{error}: {errorDescription}";
                    WriteOAuthLog($"Authorization endpoint returned error: {detail}");
                    tcs.TrySetException(new InvalidOperationException($"OAuth authorization error: {detail}"));
                    await WriteResponse(context, 400, detail);
                    return;
                }

                if (!string.Equals(state, expectedState, StringComparison.Ordinal))
                {
                    WriteOAuthLog(
                        $"State mismatch. expectedLength={expectedState.Length} actualLength={state.Length} " +
                        $"actualStatePresent={!string.IsNullOrWhiteSpace(state)}");
                    tcs.TrySetException(new InvalidOperationException("OAuth callback state does not match expected value."));
                    await WriteResponse(context, 400, "OAuth state verification failed.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(code))
                {
                    WriteOAuthLog("Callback did not include authorization code.");
                    tcs.TrySetException(new InvalidOperationException("OAuth callback did not include authorization code."));
                    await WriteResponse(context, 400, "OAuth code was not found.");
                    return;
                }

                if (!tcs.TrySetResult(code))
                {
                    WriteOAuthLog("Callback code was already handled. Ignore duplicate request.");
                }
                await WriteResponse(context, 200, "Authorization completed. You can close this window.");
            }
            catch (Exception ex)
            {
                WriteOAuthLog("Callback handler failed: " + ex.ToString());
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

        app.MapGet("/", context =>
        {
            WriteOAuthLog("Root path requested. The listener is alive, but this is not the configured OAuth callback path.");
            return context.Response.WriteAsync("OAuth callback is waiting.");
        });
    }

    private static void RegisterRequestDiagnostics(WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            WriteOAuthLog(
                $"Request pipeline entered. method={context.Request.Method} scheme={context.Request.Scheme} " +
                $"host={context.Request.Host} path={context.Request.Path} rawQueryLength={context.Request.QueryString.Value?.Length ?? 0}");

            try
            {
                await next(context);
                WriteOAuthLog(
                    $"Request pipeline completed. method={context.Request.Method} path={context.Request.Path} " +
                    $"statusCode={context.Response.StatusCode}");
            }
            catch (Exception ex)
            {
                WriteOAuthLog("Request pipeline failed: " + ex.ToString());
                throw;
            }
        });
    }

    private static IQueryCollection ReadQueryWithDiagnostics(HttpContext context)
    {
        try
        {
            var query = context.Request.Query;
            WriteOAuthLog(FormatQueryDiagnostics(query));
            return query;
        }
        catch (Exception ex)
        {
            WriteOAuthLog(
                $"Query parse failed. rawQueryLength={context.Request.QueryString.Value?.Length ?? 0} exception={ex}");
            throw;
        }
    }

    private static string FormatQueryDiagnostics(HttpContext context)
    {
        try
        {
            return FormatQueryDiagnostics(context.Request.Query);
        }
        catch (Exception ex)
        {
            return $"queryDiagnostics=parseFailed rawQueryLength={context.Request.QueryString.Value?.Length ?? 0} exception={ex.GetType().Name}: {ex.Message}";
        }
    }

    private static string FormatQueryDiagnostics(IQueryCollection query)
    {
        var error = query["error"].ToString();
        var errorDescription = query["error_description"].ToString();
        var state = query["state"].ToString();
        var code = query["code"].ToString();

        return
            $"queryDiagnostics=errorPresent={!string.IsNullOrWhiteSpace(error)} " +
            $"error={FormatOptionalValue(error)} " +
            $"errorDescription={FormatOptionalValue(errorDescription)} " +
            $"statePresent={!string.IsNullOrWhiteSpace(state)} stateLength={state.Length} " +
            $"codePresent={!string.IsNullOrWhiteSpace(code)} codeLength={code.Length}";
    }

    private static string FormatOptionalValue(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "<empty>" : value;
    }

    private static void WriteOAuthLog(string message)
    {
        Console.WriteLine($"[OAuth] {message}");
    }
}
