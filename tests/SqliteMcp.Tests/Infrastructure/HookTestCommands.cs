namespace SqliteMcp.Tests.Infrastructure;

/// <summary>
/// Cross-platform shell snippets for CliHookRunner integration tests.
/// </summary>
internal static class HookTestCommands
{
    public static string SleepSeconds(int seconds) =>
        OperatingSystem.IsWindows()
            ? $"timeout /t {seconds} /nobreak >nul"
            : $"sleep {seconds}";

    public static string ExitNonZero() =>
        OperatingSystem.IsWindows()
            ? "exit /b 1"
            : "exit 1";

    public static string FloodStdout(int lines) =>
        OperatingSystem.IsWindows()
            ? $"for /L %i in (1,1,{lines}) do @echo line %i"
            : $"i=1; while [ $i -le {lines} ]; do echo line $i; i=$((i+1)); done";

    public static string WriteTextToFile(string targetFile) =>
        OperatingSystem.IsWindows()
            ? $"powershell -NoProfile -Command \"[IO.File]::WriteAllText('{EscapeForPowerShellSingleQuoted(targetFile)}', '{{dbPath}}')\""
            : $"printf %s \"{{dbPath}}\" > \"{EscapeForShDoubleQuoted(targetFile)}\"";

    public static string WriteFixedTextToFile(string targetFile, string text) =>
        OperatingSystem.IsWindows()
            ? $"powershell -NoProfile -Command \"[IO.File]::WriteAllText('{EscapeForPowerShellSingleQuoted(targetFile)}', '{EscapeForPowerShellSingleQuoted(text)}')\""
            : $"printf %s \"{EscapeForShDoubleQuoted(text)}\" > \"{EscapeForShDoubleQuoted(targetFile)}\"";

    public static string TryDeleteFile() =>
        OperatingSystem.IsWindows()
            ? "del /f /q \"{dbPath}\" 2>nul"
            : "rm -f \"{dbPath}\"";

    public static string SpawnDetachedSleep(int seconds) =>
        OperatingSystem.IsWindows()
            ? $"start /B cmd /c \"timeout /t {seconds} /nobreak >nul\""
            : $"sh -c '(sleep {seconds}) &'";

    private static string EscapeForPowerShellSingleQuoted(string value) =>
        value.Replace("'", "''", StringComparison.Ordinal);

    private static string EscapeForShDoubleQuoted(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
}
