using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

// The IDE bridge's own MCP server. It speaks MCP over stdio and exposes the bridge tools.
// Tools are added in the migration stages; this stage only starts an empty server.
var builder = Host.CreateApplicationBuilder(args);
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport();

await builder.Build().RunAsync();
