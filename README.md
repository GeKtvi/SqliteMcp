# SqliteMcp

Cross-platform C# stdio [MCP](https://modelcontextprotocol.io/) server for SQLite (Windows, Linux, macOS). A port of [mcp-sqlite](https://github.com/jparkerweb/mcp-sqlite) with keyed multi-DB connections and an explicit connection lifecycle so database files can be closed and unlocked when agents need to delete, move, or replace them — or when the DB is part of a git repo and must be free for checkout/pull (the motivating case for this port).

This project is designed for my own use, but anyone is welcome to use it. Feel free to open issues or pull requests if you want to.

## Requirements

- **Build:** [.NET 10](https://dotnet.microsoft.com/download) SDK
- **Run:** .NET 10 runtime (framework-dependent publish), or a Native AOT binary with no runtime install

## Features
- Keyed connection dictionary (`connectionKey` → open `SqliteConnection`)
- Optional default DB from `--default-db` / `DefaultDbPath` (stored at startup, **lazy-opened** on first use)
- `open_db` / `close_db` / `close_all` to manage and release file locks
- Node-parity tools: `query`, `list_tables`, `get_table_schema`, CRUD helpers
- Identifier validation for CRUD (table/column checks, quoted identifiers)

## Platform

- **Runtime:** .NET 10 — runs on Windows, Linux, and macOS
- **Transport:** stdio MCP (Cursor and other hosts spawn the process locally on any OS)
- **File locks:** `close_db` / `close_all` release SQLite handles on all platforms; this is especially noticeable on Windows, where an open handle often blocks delete/move until closed

## Build

```bash
dotnet build src/SqliteMcp/SqliteMcp.csproj
dotnet test tests/SqliteMcp.Tests/SqliteMcp.Tests.csproj
```

### Framework-dependent publish

```bash
dotnet publish src/SqliteMcp/SqliteMcp.csproj -c Release -o publish
```

Published executable: `publish/SqliteMcp` (Linux/macOS) or `publish/SqliteMcp.exe` (Windows). Requires .NET 10 runtime on the host.

### Native AOT publish (single-file, no runtime)

The project sets `PublishAot=true`. Native publish produces a self-contained binary (no .NET runtime required).

**Windows prerequisites:** Visual Studio 2022 Build Tools with **Desktop development with C++** (MSVC + Windows SDK).

```bash
dotnet publish src/SqliteMcp/SqliteMcp.csproj -c Release -r win-x64
# Linux:  -r linux-x64
# macOS:  -r osx-arm64  or  -r osx-x64
```

Output: `src/SqliteMcp/bin/Release/net10.0/<rid>/publish/SqliteMcp(.exe)`.

Tool JSON uses source-generated `AppJsonContext`; MCP tools are registered explicitly via `WithTools<T>` (required for AOT — no assembly scanning).

## Cursor / MCP config

Pass `--default-db` to set the **default database** path. Tools that omit `connectionKey` use connection key `"default"` against this file (lazy-opened on first use). Additional databases still require `open_db` with their own keys.

**Windows**

```json
{
  "mcpServers": {
    "sqlite": {
      "command": "C:/path/to/publish/SqliteMcp.exe",
      "args": ["--default-db", "C:/data/app.db"]
    }
  }
}
```

The second `--default-db` argument is the default DB file path.

**Linux / macOS**

```json
{
  "mcpServers": {
    "sqlite": {
      "command": "/path/to/publish/SqliteMcp",
      "args": ["--default-db", "/home/user/data/app.db"]
    }
  }
}
```

The second `--default-db` argument is the default DB file path.

Alternatively, set `DefaultDbPath` in `appsettings.json` instead of `--default-db`. The default path is stored at startup but the file is not opened until the first tool call that needs it, or until `open_db` targets the default slot.
## Connection model

| Call | Behavior |
|------|----------|
| No `connectionKey` + default configured | Use key `default`; lazy-open if needed |
| No `connectionKey` + no default | Error |
| `connectionKey` set | Use that dictionary entry only (must `open_db` first) |

### `open_db`

- Opens `path` under `connectionKey` (omit key → `default`, and updates default path)
- Same key + same path → reuse
- Same key + different path → **error**

### Closing / releasing files

- `close_db` — close one connection (omit key → `default`)
- `close_all` — **WARNING:** closes every open connection and releases all locks; default path config is kept

After close, the SQLite file can be deleted, moved, or used by another process on any platform.

## Tools

| Tool | Purpose |
|------|---------|
| `open_db` | Open / register a DB under a key |
| `close_db` | Release one connection |
| `close_all` | Release all connections |
| `list_connections` | List open keys and paths |
| `db_info` | Path, size, table count |
| `query` | Raw SQL (`?` parameters); SELECT → rows, else `{ changes, lastInsertRowId }` |
| `list_tables` | User tables |
| `get_table_schema` | `PRAGMA table_info` |
| `create_record` / `read_records` / `update_records` / `delete_records` | CRUD; update/delete reject empty conditions |

All data tools accept optional `connectionKey`.

## Example agent flow

1. `list_tables` (uses default if `--default-db` was set)
2. `open_db` path=`other.db` connectionKey=`analytics`
3. `query` sql=`SELECT ...` connectionKey=`analytics`
4. `close_db` connectionKey=`analytics` when the file must be unlocked

## License

MIT — see [LICENSE](LICENSE).
