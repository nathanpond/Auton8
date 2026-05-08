using AutoNate.Web.Services.ExternalConnections;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

public sealed class DataProtectionConnectionSecretProtectorTests
{
    [Fact]
    public void Protect_then_reveal_returns_the_original_plaintext()
    {
        var protector = CreateProtector();

        var plaintext = "sk-ant-api03-very-secret-key-12345";
        var ciphertext = protector.Protect(plaintext);

        Assert.NotNull(ciphertext);
        Assert.NotEqual(plaintext, System.Text.Encoding.UTF8.GetString(ciphertext));

        var revealed = protector.Reveal(ciphertext);
        Assert.Equal(plaintext, revealed);
    }

    [Fact]
    public void Fingerprint_includes_first_and_last_four_characters_and_a_sha256_prefix()
    {
        var protector = CreateProtector();

        var fp = protector.Fingerprint("sk-ant-api03-XYZW");

        // first4 = "sk-a", last4 = "XYZW", and the sha256 prefix is 8 hex chars.
        Assert.StartsWith("sk-a…XYZW (sha256:", fp);
        Assert.EndsWith(")", fp);
        Assert.Contains("sha256:", fp);
    }

    [Fact]
    public void Fingerprint_is_deterministic_for_the_same_input()
    {
        var protector = CreateProtector();

        var a = protector.Fingerprint("hello-world");
        var b = protector.Fingerprint("hello-world");

        Assert.Equal(a, b);
    }

    [Fact]
    public void Fingerprint_differs_for_different_inputs_even_when_visible_chars_match()
    {
        var protector = CreateProtector();

        // Two strings whose first 4 + last 4 chars collide should still produce
        // distinct fingerprints because the sha256 prefix takes the full input
        // into account.
        var a = protector.Fingerprint("abcdMIDDLE1WXYZ");
        var b = protector.Fingerprint("abcdMIDDLE2WXYZ");

        Assert.NotEqual(a, b);
    }

    private static DataProtectionConnectionSecretProtector CreateProtector()
    {
        var services = new ServiceCollection();
        services.AddDataProtection();
        var sp = services.BuildServiceProvider();
        return new DataProtectionConnectionSecretProtector(sp.GetRequiredService<IDataProtectionProvider>());
    }
}
