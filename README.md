# SperryDev.Ide-Bridge

Standalone MCP server and IDE integrations for Visual Studio and VS Code. The server routes IDE tools to registered IDE instances and also provides terminal session tools.

## Components

- `src/SperryDev.Ide.Bridge.Mcp` — MCP server for IDE and terminal tools.
- `src/SperryDev.Ide.Bridge.Vsix` — Visual Studio 2026 extension.
- `vscode/` — VS Code extension and bridge implementation.
- `src/TokenSaver.LogHelper` — log parsing and query support used by the bridge.
- `src/TokenSaver.TerminalControl` — Windows Terminal session management.

## Documentation

- [IDE bridge tools and setup](https://github.com/rsperry79/TokenSaver/blob/dev/docs/ide-bridge.md)
- [Protocol conformance](conformance/protocol.md)
- [Terminal session tool contract](docs/terminal-session-contract.md)
- [Bridge self-test checklist](docs/self_test.md)
- [Remaining work plan](docs/plans/remaining-work-2026-11-19.md)
- [Windows Terminal standard session plan](docs/plans/wt-standard-session.md)

The bridge follows the shared [protocol fixtures](conformance/fixtures.json). See the linked TokenSaver guide for extension installation and live IDE setup.
