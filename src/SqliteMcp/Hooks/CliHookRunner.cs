using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SqliteMcp.Hooks;

public sealed class CliHookRunner(IOptions<HookOptions> options, ILogger<CliHookRunner> logger) : ICliHookRunner
{
    private const int MaxSqlPlaceholderLength = 2000;

    private readonly HookOptions _options = options.Value;

    public void Run(HookEventKind eventKind, HookPhase phase, HookContext context)
    {
        var eventOptions = GetEventOptions(eventKind);
        var command = phase == HookPhase.Before ? eventOptions.Before : eventOptions.After;
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        var resolvedCommand = ApplyPlaceholders(command, context);
        var timeout = eventOptions.Timeout ?? _options.Timeout;

        try
        {
            ExecuteCommand(eventKind, phase, resolvedCommand, timeout);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Hook {EventKind}.{Phase} failed (non-fatal).",
                eventKind,
                phase);
        }
    }

    private void ExecuteCommand(HookEventKind eventKind, HookPhase phase, string command, TimeSpan? timeout)
    {
        using var process = new Process
        {
            StartInfo = CreateStartInfo(command),
            EnableRaisingEvents = true
        };

        var output = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                output.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                output.AppendLine(e.Data);
            }
        };

        if (!process.Start())
        {
            logger.LogWarning(
                "Hook {EventKind}.{Phase} could not start process (non-fatal).",
                eventKind,
                phase);
            return;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var exited = timeout is null
            ? WaitForExit(process)
            : WaitForExit(process, timeout.Value);

        if (!exited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Hook {EventKind}.{Phase} timed out after {Timeout} and could not be killed (non-fatal).",
                    eventKind,
                    phase,
                    timeout);
                return;
            }

            logger.LogWarning(
                "Hook {EventKind}.{Phase} timed out after {Timeout} (non-fatal). Output: {Output}",
                eventKind,
                phase,
                timeout,
                output.ToString().Trim());
            return;
        }

        if (process.ExitCode != 0)
        {
            logger.LogWarning(
                "Hook {EventKind}.{Phase} exited with code {ExitCode} (non-fatal). Output: {Output}",
                eventKind,
                phase,
                process.ExitCode,
                output.ToString().Trim());
        }
    }

    private static bool WaitForExit(Process process)
    {
        process.WaitForExit();
        return true;
    }

    private static bool WaitForExit(Process process, TimeSpan timeout)
    {
        return process.WaitForExit((int)Math.Min(timeout.TotalMilliseconds, int.MaxValue));
    }

    private static ProcessStartInfo CreateStartInfo(string command)
    {
        if (OperatingSystem.IsWindows())
        {
            return new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {command}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/sh",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(command);
        return startInfo;
    }

    /// <summary>
    /// Escapes a value for embedding inside a single-quoted POSIX shell literal (test helper).
    /// Production hook execution passes the full command via <see cref="ProcessStartInfo.ArgumentList"/>.
    /// </summary>
    internal static string QuoteForShSingleQuoted(string value) =>
        "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";

    private HookEventOptions GetEventOptions(HookEventKind eventKind) =>
        eventKind switch
        {
            HookEventKind.Open => _options.Open,
            HookEventKind.Close => _options.Close,
            HookEventKind.CloseAll => _options.CloseAll,
            HookEventKind.Query => _options.Query,
            _ => throw new ArgumentOutOfRangeException(nameof(eventKind), eventKind, null)
        };

    internal static string ApplyPlaceholders(string command, HookContext context)
    {
        var result = command;

        if (context.ConnectionKey is not null)
        {
            result = result.Replace("{connectionKey}", context.ConnectionKey, StringComparison.Ordinal);
        }

        if (context.DbPath is not null)
        {
            result = result.Replace("{dbPath}", context.DbPath, StringComparison.Ordinal);
        }

        if (context.Sql is not null)
        {
            var sql = context.Sql.Length > MaxSqlPlaceholderLength
                ? context.Sql[..MaxSqlPlaceholderLength]
                : context.Sql;
            result = result.Replace("{sql}", sql, StringComparison.Ordinal);
        }

        if (context.ClosedKeys is not null)
        {
            result = result.Replace("{closedKeys}", context.ClosedKeys, StringComparison.Ordinal);
        }

        return result;
    }
}
