using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SperryDev.Ide.Bridge;
using Task = System.Threading.Tasks.Task;
using Process = System.Diagnostics.Process;

namespace SperryDev.Ide.Bridge.Vsix;

/// <summary>Starts this Visual Studio instance's IDE bridge when the package loads.</summary>
[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[InstalledProductRegistration("SperryDev IDE Bridge", "Named-pipe bridge for IDE tools", "0.1.0")]
[Guid(BridgePackage.PackageGuidString)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideAutoLoad(VSConstants.UICONTEXT.NoSolution_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExists_string, PackageAutoLoadFlags.BackgroundLoad)]
public sealed class BridgePackage : AsyncPackage
{
    public const string PackageGuidString = "774B5915-DC74-4FB8-AE6F-A8EF4A7187F7";
    private static readonly TimeSpan WatchdogInterval = TimeSpan.FromSeconds(10);

    private BridgeServer? _server;
    private SolutionEvents? _solutionEvents;
    private volatile string? _solutionPath;
    private System.Threading.Timer? _watchdog;
    private int _pid;

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await base.InitializeAsync(cancellationToken, progress);
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        using (var process = Process.GetCurrentProcess())
        {
            _pid = process.Id;
        }

        var dte = (DTE2?)await GetServiceAsync(typeof(DTE))
            ?? throw new InvalidOperationException("Visual Studio DTE service is unavailable.");
        _solutionPath = NullIfEmpty(dte.Solution?.FullName);

        var actions = new IdeActions(dte);
        var pipeName = InstanceRegistry.PipeNameFor(_pid);
        _server = new BridgeServer(
            pipeName,
            async (action, args) =>
            {
                await JoinableTaskFactory.SwitchToMainThreadAsync();
                return actions.Invoke(action, args);
            },
            () => _solutionPath,
            new DebugBridgeLogger());
        BridgeControl.Attach(_server, _pid, pipeName, () => _solutionPath);
        BridgeControl.Start();

        _solutionEvents = dte.Events.SolutionEvents;
#pragma warning disable VSTHRD010 // DTE solution events are raised on the UI thread.
        _solutionEvents.Opened += () => UpdateSolutionPath(dte.Solution?.FullName);
        _solutionEvents.AfterClosing += () => UpdateSolutionPath(null);
#pragma warning restore VSTHRD010

        var pid = _pid;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => InstanceRegistry.Remove(pid);
        _watchdog = new System.Threading.Timer(_ => Watchdog(), null, WatchdogInterval, WatchdogInterval);
        await BridgeStatusCommand.InitializeAsync(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _watchdog?.Dispose();
            if (_pid != 0)
            {
                InstanceRegistry.Remove(_pid);
            }
            _server?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void Watchdog()
    {
        try
        {
            if (BridgeControl.UserStopped)
            {
                return;
            }

            if (_server is { IsRunning: false })
            {
                _server.Start();
            }

            InstanceRegistry.Write(_pid, _solutionPath);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[IdeBridge] Watchdog error: {ex}");
        }
    }

    private void UpdateSolutionPath(string? solutionPath)
    {
        _solutionPath = NullIfEmpty(solutionPath);
        InstanceRegistry.Write(_pid, _solutionPath);
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}

/// <summary>Writes bridge diagnostics to Debug output.</summary>
internal sealed class DebugBridgeLogger : IBridgeLogger
{
    public void LogDebug(string message) => Debug.WriteLine($"[IdeBridge] {message}");
    public void LogInfo(string message) => Debug.WriteLine($"[IdeBridge] {message}");
    public void LogError(string message) => Debug.WriteLine($"[IdeBridge] ERROR {message}");
    public void LogException(Exception exception) => Debug.WriteLine($"[IdeBridge] EXCEPTION {exception}");
}

