using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TokenSaver.MCP.Services;
using TokenSaver.MCP.Tools;
using TokenSaver.TerminalControl.Contracts;
using TokenSaver.TerminalControl.Core;
using TokenSaver.TerminalControl.Discovery;

// The IDE bridge's own MCP server. It speaks MCP over stdio and exposes the bridge tools.
var builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<IdeToolsOptions>(builder.Configuration.GetSection("IdeTools"));
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<IdeTools>()
    .WithTools<TerminalSessionTools>();
builder.Services.AddSingleton<ISessionRegistry, SessionRegistry>();
builder.Services.AddSingleton<ITerminalConfigurationDiscovery, TerminalConfigurationDiscovery>();
builder.Services.AddSingleton<ITerminalSessionManager, TerminalSessionManager>();

await builder.Build().RunAsync();
