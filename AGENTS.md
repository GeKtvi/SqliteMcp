# Agent notes — SqliteMcp

This repo is a cross-platform C# stdio MCP server for SQLite (Windows, Linux, macOS).

## Connection rules

- Connections live in a process-wide dictionary keyed by `connectionKey`.
- Omit `connectionKey` only when a default DB was configured (`--default-db` / `DefaultDbPath`); first use lazy-opens under key `default`.
- If there is no default and no key → error. Call `open_db`.
- When `connectionKey` is set, ignore the default; the key must already be open.
- `open_db` with an existing key and a **different** path → error. Close first or pick another key.
- Prefer `list_connections` before opening blindly.

## Releasing file locks

Call `close_db` or `close_all` before deleting/moving a DB file (required on all platforms when the MCP still holds the file open). `close_all` closes every connection; its tool description warns about that.

## Implementation conventions

- Route all DB access through `SqliteConnectionManager`.
- Tools stay thin; SQL helpers live in `Sql/`.
- Never write logs to stdout (stdio MCP); logging goes to stderr only.
- CRUD must validate table/column names and reject empty update/delete conditions.
- Do not invent MCP “sessions” on top of stdio; keys are connection handles, not chat sessions.

## AOT / JSON

- `PublishAot=true` in the project; register tools with explicit `.WithTools<T>(AppJsonContext.Default.Options)` in `Program.cs`.
- Tool responses use typed DTOs in `Json/ToolResponses.cs` + `[JsonSerializable]` on `AppJsonContext`.
- Register MCP tool parameter types on `AppJsonContext` too (e.g. `string`, `double?`, `JsonElement?`).
- Dynamic query rows use `JsonNode` in `SqliteCommandRunner`; fixed-shape results use source-gen `ToJson<T>`.
