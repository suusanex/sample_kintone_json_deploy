using System.Diagnostics;
using System.Security.Cryptography;
using KintoneJsDeploy.Cli.Options;
using KintoneJsDeploy.Cli.Services;

namespace KintoneJsDeploy.Cli;

internal sealed class Program
{
    private const string GetTokenCommand = "get-token";
    private const string DeployCommand = "deploy";
    private const string CheckCommand = "check";

    public static async Task<int> Main(string[] args)
    {
        LoadDotEnv();

        using var cancellationSource = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellationSource.Cancel();
        };

        try
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return 1;
            }

            return args[0].ToLowerInvariant() switch
            {
                GetTokenCommand => await RunGetTokenAsync(cancellationSource.Token),
                DeployCommand => await RunDeployAsync(cancellationSource.Token),
                CheckCommand => RunCheck(),
                _ => ThrowWithUsage("Unknown command."),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[TRACE] " + ex.ToString());
            return 1;
        }
    }

    private static async Task<int> RunGetTokenAsync(CancellationToken cancellationToken)
    {
        var options = GetTokenCommandOptions.FromEnvironment();
        ThrowIfCallbackPortInUse(options.RedirectUri.Port);
        var state = GenerateState();
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        var oauthService = new OAuthService(httpClient);
        var authUrl = oauthService.BuildAuthorizationUrl(options, state);
        var timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);

        Console.WriteLine("Open the following URL and allow the application:");
        Console.WriteLine(authUrl);

        using var callbackListener = new LocalOAuthCallbackListener(options.RedirectUri);
        var waitTask = callbackListener.WaitForCodeAsync(state, timeout, cancellationToken);
        TryOpenBrowser(authUrl);
        var authorizationCode = await waitTask;

        var token = await oauthService.ExchangeAuthorizationCodeForAccessTokenAsync(
            options,
            authorizationCode,
            cancellationToken);

        Console.WriteLine("OAuth token exchange succeeded.");
        if (!string.IsNullOrWhiteSpace(token.Scope))
        {
            Console.WriteLine($"scope: {token.Scope}");
        }

        if (!string.IsNullOrWhiteSpace(token.RefreshToken))
        {
            Console.WriteLine($"refresh token: {token.RefreshToken}");
            Console.WriteLine("Important: save this refresh token to environment or GitHub Secrets.");
        }
        else
        {
            Console.WriteLine("No refresh_token returned by endpoint.");
        }

        Console.WriteLine($"expires_in: {token.ExpiresIn}");
        return 0;
    }

    private static async Task<int> RunDeployAsync(CancellationToken cancellationToken)
    {
        var options = DeployCommandOptions.FromEnvironment();
        var jsPath = ResolveDesktopJsPath();

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

        var oauthService = new OAuthService(httpClient);
        var token = await oauthService.RefreshAccessTokenAsync(
            options.Subdomain,
            options.ClientId,
            options.ClientSecret,
            options.RefreshToken,
            cancellationToken);

        var kintoneClient = new KintoneApiService(httpClient, options.Subdomain);
        var fileKey = await kintoneClient.UploadJavaScriptFileAsync(jsPath, token.AccessToken, cancellationToken);
        Console.WriteLine($"fileKey: {fileKey}");

        var revision = await kintoneClient.UpdatePreviewCustomizationAsync(
            options.AppId,
            fileKey,
            token.AccessToken,
            cancellationToken);
        Console.WriteLine($"revision: {revision}");

        await kintoneClient.DeployPreviewSettingsAsync(options.AppId, token.AccessToken, cancellationToken);
        Console.WriteLine("deploy request accepted.");

        var status = await kintoneClient.PollDeployStatusUntilSuccessAsync(
            options.AppId,
            token.AccessToken,
            cancellationToken,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromSeconds(3));
        Console.WriteLine($"deploy status: {status}");
        Console.WriteLine("deploy completed.");
        return 0;
    }

    private static int RunCheck()
    {
        var files = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "kintone-js-oauth-ci-poc", "src", "desktop.js"),
            Path.Combine(Directory.GetCurrentDirectory(), "src", "desktop.js"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "src", "desktop.js"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "src", "desktop.js")
        };

        var candidatePath = files.FirstOrDefault(File.Exists)
                            ?? throw new FileNotFoundException("desktop.js not found in expected locations.");

        _ = File.ReadAllText(candidatePath);
        Console.WriteLine("check passed.");
        return 0;
    }

    private static string ResolveDesktopJsPath()
    {
        var customizedPath = Environment.GetEnvironmentVariable("KINTONE_DESKTOP_JS_PATH");
        if (!string.IsNullOrWhiteSpace(customizedPath) && File.Exists(customizedPath))
        {
            return Path.GetFullPath(customizedPath);
        }

        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "kintone-js-oauth-ci-poc", "src", "desktop.js"),
            Path.Combine(Directory.GetCurrentDirectory(), "src", "desktop.js"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "src", "desktop.js"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "src", "desktop.js")
        };

        var found = candidates.FirstOrDefault(path => File.Exists(path))
            ?? throw new FileNotFoundException("desktop.js not found. Set KINTONE_DESKTOP_JS_PATH if needed.");

        return Path.GetFullPath(found);
    }

    private static string GenerateState()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static void TryOpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            Console.WriteLine("Browser could not be opened automatically. Open the printed URL manually.");
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project src/KintoneJsDeploy.Cli -- get-token");
        Console.WriteLine("  dotnet run --project src/KintoneJsDeploy.Cli -- deploy");
        Console.WriteLine("  dotnet run --project src/KintoneJsDeploy.Cli -- check");
        Console.WriteLine();
        Console.WriteLine("Notes:");
        Console.WriteLine("  Secrets are never printed to stdout.");
        Console.WriteLine("  Use .env file or OS environment variables.");
    }

    private static int ThrowWithUsage(string message)
    {
        Console.WriteLine(message);
        PrintUsage();
        return 1;
    }

    private static void LoadDotEnv()
    {
        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), ".env"),
            Path.Combine(Directory.GetCurrentDirectory(), "kintone-js-oauth-ci-poc", ".env"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", ".env"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", ".env")
        };

        var envFile = candidates.FirstOrDefault(File.Exists);
        if (envFile is null)
        {
            return;
        }

        var lines = File.ReadAllLines(envFile);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (Environment.GetEnvironmentVariable(key) is null)
            {
                Environment.SetEnvironmentVariable(key, TrimQuotedValue(value));
            }
        }
    }

    private static string TrimQuotedValue(string value)
    {
        if (value.Length >= 2 &&
            ((value.StartsWith("\"") && value.EndsWith("\"")) || (value.StartsWith("'") && value.EndsWith("'"))))
        {
            return value[1..^1];
        }

        return value;
    }

    private static void ThrowIfCallbackPortInUse(int port)
    {
        var listeners = GetListeningPidsForPort(port);
        if (listeners.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Local callback port {port} is already in use. " +
            $"Possible leftover OAuth listener processes: {string.Join(", ", listeners)}. " +
            "Stop previous get-token flow and retry.");
    }

    private static IReadOnlyCollection<int> GetListeningPidsForPort(int port)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netstat",
                Arguments = "-ano -p TCP",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return Array.Empty<int>();
            }

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(3000);

            if (process.ExitCode != 0)
            {
                Console.Error.WriteLine($"[OAuth] netstat returned non-zero status: {error}");
                return Array.Empty<int>();
            }

            var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            return lines
                .Select(line => line.Trim())
                .Where(line => line.StartsWith("TCP", StringComparison.OrdinalIgnoreCase))
                .Select(ParseListeningPortAndPidFromNetstatLine)
                .Where(x => x is not null && x.Value.port == port && x.Value.pid != 0)
                .Select(x => x!.Value.pid)
                .Distinct()
                .ToList();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[OAuth] Failed to inspect local ports: {ex.Message}");
            return Array.Empty<int>();
        }
    }

    private static (int pid, int port)? ParseListeningPortAndPidFromNetstatLine(string line)
    {
        try
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5 || !string.Equals(parts[3], "LISTENING", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var localPort = ExtractPort(parts[1]);
            if (string.IsNullOrWhiteSpace(localPort))
            {
                return null;
            }

            if (!int.TryParse(localPort, out var port))
            {
                return null;
            }

            if (!int.TryParse(parts[^1], out var pid))
            {
                return null;
            }

            return (pid, port);
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractPort(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return null;
        }

        if (endpoint.StartsWith("["))
        {
            var close = endpoint.IndexOf(']');
            if (close < 0 || close + 2 > endpoint.Length)
            {
                return null;
            }

            return endpoint[(close + 2)..];
        }

        var colon = endpoint.LastIndexOf(':');
        if (colon < 0 || colon + 1 >= endpoint.Length)
        {
            return null;
        }

        return endpoint[(colon + 1)..];
    }
}
