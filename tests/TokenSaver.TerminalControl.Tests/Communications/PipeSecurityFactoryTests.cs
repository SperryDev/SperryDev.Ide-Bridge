namespace TokenSaver.TerminalControl.Tests;

using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Xunit;
using TokenSaver.TerminalControl.Output;

public sealed class PipeSecurityFactoryTests
{
    [Fact]
    public void Create_GrantsClientReadWriteAndNoOneElse()
    {
        var client = new SecurityIdentifier("S-1-5-21-1-2-3-1001");

        var security = PipeSecurityFactory.Create(client);
        var rules = security.GetAccessRules(true, false, typeof(SecurityIdentifier))
            .Cast<PipeAccessRule>()
            .ToList();

        var clientRule = Assert.Single(rules, r => r.IdentityReference.Value == client.Value);
        Assert.Equal(PipeAccessRights.ReadWrite, clientRule.PipeAccessRights & PipeAccessRights.ReadWrite);
        Assert.DoesNotContain(rules, r => r.IdentityReference.Value == "S-1-1-0");   // Everyone
        Assert.DoesNotContain(rules, r => r.IdentityReference.Value == "S-1-5-11");  // Authenticated Users
    }

    [Fact]
    public void Create_WithoutClient_GrantsOnlySystemAndAdministrators()
    {
        var security = PipeSecurityFactory.Create(null);
        var sids = security.GetAccessRules(true, false, typeof(SecurityIdentifier))
            .Cast<PipeAccessRule>()
            .Select(r => r.IdentityReference.Value)
            .ToHashSet();

        Assert.Equal(2, sids.Count);
        Assert.Contains("S-1-5-18", sids);       // LocalSystem
        Assert.Contains("S-1-5-32-544", sids);   // Administrators
    }
}
