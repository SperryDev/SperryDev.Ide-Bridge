using System.Reflection;
using Xunit;

namespace TokenSaver.LogHelper.Tests;

public sealed class ScaffoldTests
{
    private static readonly string[] ForbiddenPrefixes =
    [
        "Microsoft.AspNetCore",
        "EnvDTE",
        "TokenSaver.MCP",
        "TokenSaver.Common",
        "SperryDev",
    ];

    [Fact]
    public void Assembly_HasNoAspNetIdeMcpOrCommonReferences()
    {
        var assembly = Assembly.Load("TokenSaver.LogHelper");

        var names = assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToList();

        foreach (var prefix in ForbiddenPrefixes)
        {
            Assert.DoesNotContain(names, n => n.StartsWith(prefix, StringComparison.Ordinal));
        }
    }
}
