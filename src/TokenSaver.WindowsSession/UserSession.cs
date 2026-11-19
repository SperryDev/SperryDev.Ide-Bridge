namespace TokenSaver.WindowsSession;

using static TokenSaver.WindowsSession.NativeMethods;

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;

/// <summary>
/// Token and session primitives shared by the code that starts processes on a user's desktop. A Windows service
/// (LocalSystem) runs in session 0, where nothing is visible; these calls reach the user's session instead. They need
/// SeTcbPrivilege, which LocalSystem holds, and fail with <see cref="Win32Exception"/> without it or without a user.
/// Each caller keeps its own session choice and process launch.
/// </summary>
[SupportedOSPlatform("windows")]
public static class UserSession
{
    /// <summary>Returned when no session matches.</summary>
    public const uint NoSession = 0xFFFFFFFF;

    private const uint TokenAllAccess = 0xF01FF;
    private const int SecurityImpersonation = 2;
    private const int TokenPrimary = 1;
    private const int TokenSessionIdClass = 12;
    private const int WtsActive = 0;
    private const int WtsUserName = 5;
    private const int WtsDomainName = 7;

    /// <summary>True when this process runs as LocalSystem.</summary>
    public static bool IsRunningAsLocalSystem() => WindowsIdentity.GetCurrent().IsSystem;

    /// <summary>The session of the console user, or <see cref="NoSession"/> when nobody is signed in there.</summary>
    public static uint ActiveConsoleSession() => WTSGetActiveConsoleSessionId();

    /// <summary>The first active session with a logged-on user (the console may have none, e.g. over RDP), or <see cref="NoSession"/>.</summary>
    public static uint FindFirstLoggedOnSession()
    {
        if (!WTSEnumerateSessionsW(IntPtr.Zero, 0, 1, out IntPtr infos, out int count))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "WTSEnumerateSessions failed.");
        }

        try
        {
            int size = Marshal.SizeOf<WtsSessionInfo>();
            for (int i = 0; i < count; i++)
            {
                var info = Marshal.PtrToStructure<WtsSessionInfo>(infos + i * size);
                if (info.State == WtsActive && !string.IsNullOrEmpty(QuerySessionString(info.SessionId, WtsUserName)))
                {
                    return info.SessionId;
                }
            }
        }
        finally
        {
            WTSFreeMemory(infos);
        }

        return NoSession;
    }

    /// <summary>The SID of the user logged on to <paramref name="session"/>, or null when there is none.</summary>
    public static SecurityIdentifier? GetUserSid(uint session)
    {
        var user = QuerySessionString(session, WtsUserName);
        if (string.IsNullOrEmpty(user))
        {
            return null;
        }

        var domain = QuerySessionString(session, WtsDomainName);
        var account = string.IsNullOrEmpty(domain) ? user : $"{domain}\\{user}";
        return (SecurityIdentifier)new NTAccount(account).Translate(typeof(SecurityIdentifier));
    }

    /// <summary>
    /// Duplicates the logged-on user's token as a primary token. With <paramref name="bindToSession"/> the token is
    /// bound to <paramref name="session"/>, so processes started with it run in that session.
    /// </summary>
    public static UserToken OpenPrimaryToken(uint session, bool bindToSession)
    {
        if (!WTSQueryUserToken(session, out IntPtr userToken))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "WTSQueryUserToken failed.");
        }

        try
        {
            if (!DuplicateTokenEx(userToken, TokenAllAccess, IntPtr.Zero, SecurityImpersonation, TokenPrimary, out IntPtr primary))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "DuplicateTokenEx failed.");
            }

            if (bindToSession)
            {
                uint sid = session;
                if (!SetTokenInformation(primary, TokenSessionIdClass, ref sid, sizeof(uint)))
                {
                    _ = CloseHandle(primary);
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "SetTokenInformation(TokenSessionId) failed.");
                }
            }

            return new UserToken(primary);
        }
        finally
        {
            _ = CloseHandle(userToken);
        }
    }

    /// <summary>Creates the environment block for <paramref name="token"/>; release it with <see cref="DestroyEnvironmentBlock"/>.</summary>
    public static bool TryCreateEnvironmentBlock(UserToken token, out IntPtr environment) =>
        CreateEnvironmentBlock(out environment, token.Handle, false);

    public static void DestroyEnvironmentBlock(IntPtr environment)
    {
        if (environment != IntPtr.Zero)
        {
            _ = DestroyEnvironmentBlockNative(environment);
        }
    }

    private static string? QuerySessionString(uint session, int infoClass)
    {
        if (!WTSQuerySessionInformationW(IntPtr.Zero, session, infoClass, out IntPtr buf, out _))
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(buf);
        }
        finally
        {
            WTSFreeMemory(buf);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WtsSessionInfo
    {
        public uint SessionId;
        public IntPtr pWinStationName;
        public int State;
    }

    // Forwarded to NativeMethods; UserToken releases its handle through this.
    internal static bool CloseHandle(IntPtr handle) => NativeMethods.CloseHandle(handle);
}

/// <summary>A primary user token. Dispose it when the processes started with it are no longer needed.</summary>
[SupportedOSPlatform("windows")]
public sealed class UserToken : IDisposable
{
    internal UserToken(IntPtr handle)
    {
        Handle = handle;
    }

    public IntPtr Handle { get; private set; }

    public void Dispose()
    {
        if (Handle != IntPtr.Zero)
        {
            _ = UserSession.CloseHandle(Handle);
            Handle = IntPtr.Zero;
        }
    }
}
