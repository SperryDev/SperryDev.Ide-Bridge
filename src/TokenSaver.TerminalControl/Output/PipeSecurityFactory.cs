namespace TokenSaver.TerminalControl.Output;

using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

/// <summary>
/// Builds the ACL for a session pipe. TokenSaver (LocalSystem) owns the pipe; a terminal started in the
/// logged-on user's session connects as that user, so the user's SID is granted read/write. Nobody else is
/// granted access, so other local users can neither read the command stream nor inject output.
/// </summary>
public static class PipeSecurityFactory
{
    public static PipeSecurity Create(SecurityIdentifier? clientSid)
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        if (clientSid is not null)
        {
            security.AddAccessRule(new PipeAccessRule(
                clientSid,
                PipeAccessRights.ReadWrite,
                AccessControlType.Allow));
        }

        return security;
    }
}
