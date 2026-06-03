using KintoneOAuthTokenCli;
using NUnit.Framework;

namespace KintoneOAuthTokenCli.Tests;

[TestFixture]
public class OAuthTokenAcquirerTests
{
    [Test]
    public void BuildAuthorizationUri_ContainsExpectedQuery()
    {
        var acquirer = new OAuthTokenAcquirer(new HttpClient());
        var options = new OAuthCliOptions
        {
            Subdomain = "mycompany",
            ClientId = "client-123",
            ClientSecret = "secret",
            RedirectUri = new Uri("https://localhost:54187/oauth")
        };
        var state = "test-state-123";

        var uri = acquirer.BuildAuthorizationUri(options, state);

        Assert.That(uri, Does.StartWith("https://mycompany.kintone.com/oauth2/authorization"));
        Assert.That(uri, Does.Contain("response_type=code"));
        Assert.That(uri, Does.Contain("client_id=client-123"));
        Assert.That(uri, Does.Contain("redirect_uri=https%3A%2F%2Flocalhost%3A54187%2Foauth"));
        Assert.That(uri, Does.Contain("scope=k%3Aapp_record%3Aread"));
        Assert.That(uri, Does.Contain("state=test-state-123"));
    }

    [Test]
    public void BuildAuthorizationUri_WithHostValue_UsesHostAsIs()
    {
        var acquirer = new OAuthTokenAcquirer(new HttpClient());
        var options = new OAuthCliOptions
        {
            Subdomain = "mycompany.kintone.com",
            ClientId = "client-123",
            ClientSecret = "secret",
            RedirectUri = new Uri("https://localhost:54187/oauth")
        };
        var state = "test-state-123";

        var uri = acquirer.BuildAuthorizationUri(options, state);

        Assert.That(uri, Does.StartWith("https://mycompany.kintone.com/oauth2/authorization"));
    }

    [Test]
    public void BuildAuthorizationUri_WithCustomScope_IncludesScope()
    {
        var acquirer = new OAuthTokenAcquirer(new HttpClient());
        var options = new OAuthCliOptions
        {
            Subdomain = "mycompany",
            ClientId = "client-123",
            ClientSecret = "secret",
            RedirectUri = new Uri("https://localhost:54187/oauth"),
            Scope = "k:app_record:read k:app_settings:read"
        };
        var state = "test-state-123";

        var uri = acquirer.BuildAuthorizationUri(options, state);

        Assert.That(uri, Does.StartWith("https://mycompany.kintone.com/oauth2/authorization"));
        Assert.That(uri, Does.Contain("scope=k%3Aapp_record%3Aread+k%3Aapp_settings%3Aread"));
    }

    [Test]
    public void ExtractAccessToken_WithValidResponse_ReturnsToken()
    {
        const string response = "{\"access_token\":\"token-value\",\"token_type\":\"Bearer\"}";

        var token = OAuthTokenAcquirer.ExtractAccessToken(response);

        Assert.That(token, Is.EqualTo("token-value"));
    }

    [Test]
    public void ExtractQueryParameter_WithExistingKey_ReturnsValue()
    {
        var result = OAuthTokenAcquirer.ExtractQueryParameter("?code=abc123&state=s", "code");

        Assert.That(result, Is.EqualTo("abc123"));
    }
}
