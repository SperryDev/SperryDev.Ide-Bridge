namespace TokenSaver.MCP.Services;

/// <summary>Settings the IDE tools read. The bridge MCP binds this from its own configuration.</summary>
public sealed class IdeToolsOptions
{
    /// <summary>"DotnetTest" or "TestExplorer". Empty means TestExplorer.</summary>
    public string IdeTestRunMode { get; set; } = "";
}

