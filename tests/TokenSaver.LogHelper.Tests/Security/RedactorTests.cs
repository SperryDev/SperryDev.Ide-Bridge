using TokenSaver.LogHelper.Models;
using TokenSaver.LogHelper.Security;
using Xunit;

namespace TokenSaver.LogHelper.Tests.Security;

public sealed class RedactorTests
{
    private static readonly LogHelperOptions Enabled = new();

    [Theory]
    [InlineData("Authorization: Bearer abc.def.ghi", "abc.def.ghi", "<redacted:bearer>")]
    [InlineData("hdr eyJhbGciOi.eyJzdWIiOi.c2lnbmF0dXJl end", "eyJzdWIiOi", "<redacted:jwt>")]
    [InlineData("login password=hunter2 ok", "hunter2", "<redacted:secret>")]
    [InlineData("api_key=XYZ123 call", "XYZ123", "<redacted:secret>")]
    [InlineData("Server=db;Password=s3cret;", "s3cret", "<redacted:secret>")]
    [InlineData("user john.doe@example.com logged in", "john.doe@example.com", "<redacted:email>")]
    public void Redact_MasksSecret_WithKindMarker(string input, string secret, string marker)
    {
        string output = Redactor.Redact(input, Enabled);

        Assert.DoesNotContain(secret, output, StringComparison.Ordinal);
        Assert.Contains(marker, output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("AKIAIOSFODNN7EXAMPLE", "<redacted:cloud-key>")]
    [InlineData("key=AKIAIOSFODNN7EXAMPLE end", "<redacted:cloud-key>")]
    public void Redact_MasksCloudKeyShapes(string input, string marker)
    {
        string output = Redactor.Redact(input, Enabled);

        Assert.DoesNotContain("AKIAIOSFODNN7EXAMPLE", output, StringComparison.Ordinal);
        Assert.Contains(marker, output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("request 3f2504e0-4f89-11d3-9a0c-0305e82c3301 done")]
    [InlineData("sha256 9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08 ok")]
    [InlineData(@"opened C:\logs\2026\app.log")]
    [InlineData("commit 0a1b2c3 merged")]
    public void Redact_LeavesIdsHashesAndPaths(string input)
    {
        Assert.Equal(input, Redactor.Redact(input, Enabled));
    }

    [Fact]
    public void Disabled_LeavesTextUnchanged()
    {
        var options = new LogHelperOptions { RedactionEnabled = false };
        const string input = "password=hunter2 user a@b.com";

        Assert.Equal(input, Redactor.Redact(input, options));
    }

    [Fact]
    public void PlainMessage_Unchanged()
    {
        const string input = "user 42 failed at step three";

        Assert.Equal(input, Redactor.Redact(input, Enabled));
    }
}
