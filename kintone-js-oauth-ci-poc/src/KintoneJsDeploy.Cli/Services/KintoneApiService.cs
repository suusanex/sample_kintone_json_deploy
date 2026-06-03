using System.Net.Http.Headers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using KintoneJsDeploy.Cli.Models;

namespace KintoneJsDeploy.Cli.Services;

internal sealed class KintoneApiService(HttpClient httpClient, string subdomain)
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly Uri _baseUri = new($"https://{subdomain}.kintone.com");

    public async Task<string> UploadJavaScriptFileAsync(string jsFilePath, string accessToken, CancellationToken cancellationToken)
    {
        if (!File.Exists(jsFilePath))
        {
            throw new FileNotFoundException("desktop.js file not found.", jsFilePath);
        }

        await using var fileStream = File.OpenRead(jsFilePath);
        var fileName = Path.GetFileName(jsFilePath);
        using var form = new MultipartFormDataContent();

        var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(ResolveContentType(fileName));
        form.Add(fileContent, "file", fileName);

        var response = await SendKintoneRequestAsync(
            HttpMethod.Post,
            "/k/v1/file.json",
            accessToken,
            form,
            cancellationToken);

        using var responseForDispose = response;
        var payload = await ReadResponsePayloadAsync<FileUploadResponse>(
            responseForDispose,
            "POST /k/v1/file.json",
            cancellationToken);
        if (string.IsNullOrWhiteSpace(payload.FileKey))
        {
            throw new InvalidOperationException("fileKey is missing in file upload response.");
        }

        return payload.FileKey;
    }

    public async Task<string> UpdatePreviewCustomizationAsync(
        int appId,
        string fileKey,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            app = appId,
            scope = "ADMIN",
            desktop = new
            {
                js = new[]
                {
                    new
                    {
                        type = "FILE",
                        file = new
                        {
                            fileKey
                        }
                    }
                },
                css = Array.Empty<object>()
            },
            mobile = new
            {
                js = Array.Empty<object>(),
                css = Array.Empty<object>()
            }
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await SendKintoneRequestAsync(
            HttpMethod.Put,
            "/k/v1/preview/app/customize.json",
            accessToken,
            content,
            cancellationToken);

        using var responseForDispose = response;
        var payloadJson = await responseForDispose.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            return ExtractRevision(payloadJson);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[TRACE] Failed to parse /k/v1/preview/app/customize.json response.");
            Console.Error.WriteLine(TruncateForLog(payloadJson));
            throw new InvalidOperationException("Failed to parse preview customization response.", ex);
        }
    }

    public async Task DeployPreviewSettingsAsync(int appId, string accessToken, CancellationToken cancellationToken)
    {
        var payload = new
        {
            apps = new[]
            {
                new
                {
                    app = appId
                }
            }
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await SendKintoneRequestAsync(
            HttpMethod.Post,
            "/k/v1/preview/app/deploy.json",
            accessToken,
            content,
            cancellationToken);
    }

    public async Task<string> GetDeployStatusAsync(int appId, string accessToken, CancellationToken cancellationToken)
    {
        var uri = new Uri(_baseUri, BuildDeployStatusUri(appId));
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            response.Dispose();
            throw new InvalidOperationException(
                $"Get App Deploy Status failed. endpoint=/k/v1/preview/app/deploy.json?apps%5B0%5D={appId} statusCode={(int)response.StatusCode} response={body}");
        }

        using var responseForDispose = response;
        var payloadJson = await responseForDispose.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            return ExtractDeployStatus(payloadJson, appId);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[TRACE] Failed to parse /k/v1/preview/app/deploy.json response.");
            Console.Error.WriteLine(TruncateForLog(payloadJson));
            throw new InvalidOperationException("Failed to parse deploy status response.", ex);
        }
    }

    public async Task<string> PollDeployStatusUntilSuccessAsync(
        int appId,
        string accessToken,
        CancellationToken cancellationToken,
        TimeSpan timeout,
        TimeSpan interval)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        while (true)
        {
            var status = await GetDeployStatusAsync(appId, accessToken, linkedCts.Token);
            switch (status)
            {
                case "SUCCESS":
                    return status;
                case "FAIL":
                case "CANCEL":
                    throw new InvalidOperationException($"Deploy failed. status={status} app={appId}");
                case "PROCESSING":
                    break;
                default:
                    break;
            }

            await Task.Delay(interval, linkedCts.Token);
        }
    }

    private async Task<HttpResponseMessage> SendKintoneRequestAsync(
        HttpMethod method,
        string relativePath,
        string accessToken,
        HttpContent content,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, new Uri(_baseUri, relativePath))
        {
            Content = content
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.ParseAdd("application/json");

        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            response.Dispose();
            throw new InvalidOperationException(
                $"Request failed. endpoint={relativePath} statusCode={(int)response.StatusCode} response={body}");
        }

        return response;
    }

    private static async Task<T> ReadResponsePayloadAsync<T>(HttpResponseMessage response, string endpoint, CancellationToken cancellationToken)
        where T : class
    {
        var payloadJson = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            var payload = JsonSerializer.Deserialize<T>(
                payloadJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return payload ?? throw new InvalidOperationException("Response body was empty.");
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"[TRACE] JSON parse failed for {endpoint} to {typeof(T).Name}.");
            Console.Error.WriteLine(TruncateForLog(payloadJson));
            throw new InvalidOperationException(
                $"Response parse failed. endpoint={endpoint} targetType={typeof(T).Name}",
                ex);
        }
    }

    private static string ExtractRevision(string payloadJson)
    {
        using var json = JsonDocument.Parse(payloadJson);
        if (!json.RootElement.TryGetProperty("revision", out var revisionElement))
        {
            throw new InvalidOperationException($"Response did not include revision: {payloadJson}");
        }

        return revisionElement.ValueKind switch
        {
            JsonValueKind.String => revisionElement.GetString()
                ?? throw new InvalidOperationException($"revision was empty: {payloadJson}"),
            JsonValueKind.Number when revisionElement.TryGetInt64(out var revision)
                => revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException($"Unsupported revision value type: {revisionElement.ValueKind}, body={payloadJson}")
        };
    }

    private static string ExtractDeployStatus(string payloadJson, int targetAppId)
    {
        using var json = JsonDocument.Parse(payloadJson);
        if (!json.RootElement.TryGetProperty("apps", out var appsElement))
        {
            throw new InvalidOperationException($"Response did not include apps: {payloadJson}");
        }

        if (appsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                $"apps is not array. valueType={appsElement.ValueKind}, body={payloadJson}");
        }

        foreach (var appElement in appsElement.EnumerateArray())
        {
            if (!appElement.TryGetProperty("app", out var appValue))
            {
                continue;
            }

            var appId = ParseFlexibleInt(appValue, "apps[n].app");
            if (appId != targetAppId)
            {
                continue;
            }

            if (!appElement.TryGetProperty("status", out var statusValue))
            {
                throw new InvalidOperationException($"Status field is missing for app={targetAppId}. body={payloadJson}");
            }

            if (statusValue.ValueKind == JsonValueKind.String)
            {
                return statusValue.GetString() ?? string.Empty;
            }

            throw new InvalidOperationException(
                $"Unsupported status type for app={targetAppId}. type={statusValue.ValueKind}, body={payloadJson}");
        }

        throw new InvalidOperationException($"Deploy status response did not contain target app: {targetAppId}");
    }

    private static int ParseFlexibleInt(JsonElement value, string path)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var numberValue))
        {
            return numberValue;
        }

        if (value.ValueKind == JsonValueKind.String &&
            int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var stringValue))
        {
            return stringValue;
        }

        throw new InvalidOperationException($"Failed to parse {path}. valueType={value.ValueKind}, raw={value}");
    }

    private static string TruncateForLog(string value, int maxLength = 2048)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength] + "...(truncated)";
    }

    private static string BuildDeployStatusUri(int appId)
    {
        return $"/k/v1/preview/app/deploy.json?apps[0]={appId}";
    }

    private static string ResolveContentType(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "application/octet-stream";
        }

        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".js" => "application/javascript",
            ".css" => "text/css",
            ".html" => "text/html",
            ".json" => "application/json",
            ".txt" => "text/plain",
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream"
        };
    }
}
