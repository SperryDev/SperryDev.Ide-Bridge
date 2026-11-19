using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TokenSaver.MCP.Services;
using TokenSaver.MCP.Tools;

// The IDE bridge's own MCP server. It speaks MCP over stdio and exposes the bridge tools.
var builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<IdeToolsOptions>(builder.Configuration.GetSection("IdeTools"));
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<IdeTools>();

await builder.Build().RunAsync();
