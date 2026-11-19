using System;
using System.ComponentModel.Design;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SperryDev.Ide.Bridge;
using Task = System.Threading.Tasks.Task;

namespace SperryDev.Ide.Bridge.Vsix;

/// <summary>Tools menu commands for starting, stopping, and inspecting the IDE bridge.</summary>
internal sealed class BridgeStatusCommand
{
    private const int ShowStatusCommandId = 0x0100;
    private const int StartCommandId = 0x0101;
    private const int StopCommandId = 0x0102;
    private static readonly Guid CommandSet = new("75A66F44-1E3A-460D-A985-E49344792805");
    private static readonly Guid OutputPaneGuid = new("A3F2C4B1-6D7E-4F80-9A1B-2C3D4E5F6071");
    private const string OutputPaneTitle = "SperryDev IDE Bridge";

    private readonly AsyncPackage _package;

    private BridgeStatusCommand(AsyncPackage package, OleMenuCommandService commandService)
    {
        _package = package;
        Add(commandService, ShowStatusCommandId, ShowStatus);
        Add(commandService, StartCommandId, Start);
        Add(commandService, StopCommandId, Stop);
    }

    public static async Task InitializeAsync(AsyncPackage package)
    {
        await package.JoinableTaskFactory.SwitchToMainThreadAsync();
        var commandService = (await package.GetServiceAsync(typeof(IMenuCommandService))) as OleMenuCommandService;
        if (commandService is null)
        {
            throw new InvalidOperationException("IMenuCommandService is unavailable.");
        }

        _ = new BridgeStatusCommand(package, commandService);
    }

    private static void Add(OleMenuCommandService commandService, int commandId, EventHandler handler) =>
        commandService.AddCommand(new MenuCommand(handler, new CommandID(CommandSet, commandId)));

    private void ShowStatus(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        WriteToOutputPane($"IDE bridge: {BridgeControl.Describe()}{Environment.NewLine}");
    }

    private void Start(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        WriteToOutputPane($"{BridgeControl.Start()}{Environment.NewLine}");
    }

    private void Stop(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        WriteToOutputPane($"{BridgeControl.Stop()}{Environment.NewLine}");
    }

    private void WriteToOutputPane(string text)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var output = ServiceProvider.GetGlobalService<SVsOutputWindow, IVsOutputWindow>();
        if (output is null)
        {
            return;
        }

        var paneGuid = OutputPaneGuid;
        if (output.GetPane(ref paneGuid, out var pane) != VSConstants.S_OK || pane is null)
        {
            output.CreatePane(ref paneGuid, OutputPaneTitle, 1, 1);
            output.GetPane(ref paneGuid, out pane);
        }

        pane?.OutputStringThreadSafe(text);
    }
}

