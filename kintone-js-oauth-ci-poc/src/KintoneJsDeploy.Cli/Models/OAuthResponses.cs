using System.Text.Json.Serialization;

namespace KintoneJsDeploy.Cli.Models;

internal sealed class OAuthTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("scope")]
    public string? Scope { get; set; }
}

internal sealed class FileUploadResponse
{
    [JsonPropertyName("fileKey")]
    public string FileKey { get; set; } = string.Empty;
}

internal sealed class CustomizeUpdateResponse
{
    [JsonPropertyName("revision")]
    public string Revision { get; set; } = string.Empty;
}

internal sealed class DeployStatusResponse
{
    [JsonPropertyName("apps")]
    public DeployStatusItem[] Apps { get; set; } = System.Array.Empty<DeployStatusItem>();
}

internal sealed class DeployStatusItem
{
    [JsonPropertyName("app")]
    public string App { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
}
