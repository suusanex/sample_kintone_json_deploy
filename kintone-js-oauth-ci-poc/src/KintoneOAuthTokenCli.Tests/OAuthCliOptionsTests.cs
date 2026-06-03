using KintoneOAuthTokenCli;
using NUnit.Framework;

namespace KintoneOAuthTokenCli.Tests;

[TestFixture]
public class OAuthCliOptionsTests
{
    [Test]
    public void Parse_WithRequiredArgs_ReturnsOptions()
    {
        var args = new[]
        {
            "--subdomain", "example",
            "--client-id", "cid",
            "--client-secret", "secret"
        };

        var options = OAuthCliOptions.Parse(args);

        Assert.That(options.Subdomain, Is.EqualTo("example"));
        Assert.That(options.ClientId, Is.EqualTo("cid"));
        Assert.That(options.ClientSecret, Is.EqualTo("secret"));
        Assert.That(options.RedirectUri.ToString(), Is.EqualTo("https://localhost:54187/oauth"));
    }

    [Test]
    public void Parse_WithHttpRedirectUri_Throws()
    {
        var args = new[]
        {
            "--subdomain", "example",
            "--client-id", "cid",
            "--client-secret", "secret",
            "--redirect-uri", "http://localhost:54187/oauth"
        };

        var ex = Assert.Throws<ArgumentException>(() => OAuthCliOptions.Parse(args));

        Assert.That(ex!.Message, Does.Contain("https"));
    }
}
