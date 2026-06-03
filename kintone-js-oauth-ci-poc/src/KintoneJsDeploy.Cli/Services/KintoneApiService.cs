using System.Net.Http.Headers;
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
        using var form = new MultipartFormDataContent();
        var fileContent = new StreamContent(fileStream);
        form.Add(fileContent, "file", Path.GetFileName(jsFilePath));

        var response = await SendKintoneRequestAsync(
            HttpMethod.Post,
            "/k/v1/file.json",
            accessToken,
            form,
            cancellationToken);

        using var responseForDispose = response;
        var payload = await ReadResponsePayloadAsync<FileUploadResponse>(responseForDispose, cancellationToken);
        if (string.IsNullOrWhiteSpace(payload.FileKey))
        {
            throw new InvalidOperationException("fileKey is missing in file upload response.");
        }

        return payload.FileKey;
    }

    public async Task<int> UpdatePreviewCustomizationAsync(
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
        var customized = await ReadResponsePayloadAsync<CustomizeUpdateResponse>(responseForDispose, cancellationToken);
        return customized.Revision;
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
        var statusResponse = await ReadResponsePayloadAsync<DeployStatusResponse>(responseForDispose, cancellationToken);
        var target = statusResponse.Apps.FirstOrDefault(item => item.App == appId);
        return target?.Status ?? throw new InvalidOperationException("Deploy status response did not contain target app.");
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

    private static async Task<T> ReadResponsePayloadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
        where T : class
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<T>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            cancellationToken: cancellationToken);

        return payload ?? throw new InvalidOperationException($"Response body could not be parsed to {typeof(T).Name}.");
    }

    private static string BuildDeployStatusUri(int appId)
    {
        return $"/k/v1/preview/app/deploy.json?apps[0]={appId}";
    }
}
