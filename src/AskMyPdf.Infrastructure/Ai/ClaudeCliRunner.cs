namespace AskMyPdf.Infrastructure.Ai;

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

/// <summary>
/// Runs the Claude Code CLI as an external process and returns the parsed result.
/// All process-management logic is isolated here.
/// </summary>
public partial class ClaudeCliRunner(ClaudeCliOptions options, ILogger<ClaudeCliRunner> logger)
{
    /// <summary>Raw result from a CLI invocation.</summary>
    public record CliResult(bool Success, string? ResultText, string? Error);

    /// <summary>
    /// Executes <c>claude -p "…" --output-format json</c> and returns the parsed result text.
    /// The prompt is collapsed to a single line to avoid shell argument issues on Windows.
    /// </summary>
    public async Task<CliResult> RunAsync(string prompt, string? workingDirectory = null, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

        var collapsedPrompt = CollapseWhitespace(prompt);
        var args = BuildArgs(collapsedPrompt);
        logger.LogInformation("Starting Claude CLI: {Binary} (timeout {Timeout}s, max-turns {MaxTurns})",
            options.BinaryPath, options.TimeoutSeconds, options.MaxTurns);
        logger.LogDebug("Claude CLI prompt ({Length} chars): {Prompt}", collapsedPrompt.Length,
            collapsedPrompt.Length > 200 ? collapsedPrompt[..200] + "..." : collapsedPrompt);

        // On Windows, npm-installed CLIs are .cmd wrappers. Process.Start with
        // UseShellExecute=false can't resolve them, so run via cmd.exe.
        var isWindows = OperatingSystem.IsWindows();
        var psi = new ProcessStartInfo
        {
            FileName = isWindows ? "cmd.exe" : options.BinaryPath,
            Arguments = isWindows ? $"/c {options.BinaryPath} {args}" : args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (workingDirectory is not null)
            psi.WorkingDirectory = workingDirectory;

        using var process = new Process { StartInfo = psi };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cts.Token);

            if (process.ExitCode != 0)
            {
                var err = stderr.ToString().Trim();
                // CLI with --output-format json writes errors to stdout as JSON
                // with is_error:true — extract the actual error message
                var rawOut = stdout.ToString().Trim();
                var cliError = TryExtractJsonError(rawOut);
                var errorDetail = cliError ?? err;
                logger.LogWarning("Claude CLI exited with code {ExitCode}: {Error}", process.ExitCode, errorDetail);
                return new CliResult(false, null, $"CLI exited with code {process.ExitCode}: {errorDetail}");
            }

            var rawOutput = stdout.ToString().Trim();
            logger.LogDebug("Claude CLI raw output: {Length} chars", rawOutput.Length);

            var resultText = ParseCliOutput(rawOutput);
            if (resultText is null)
                return new CliResult(false, null, "Could not parse CLI output");

            return new CliResult(true, resultText, null);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            TryKill(process);
            logger.LogWarning("Claude CLI timed out after {Timeout}s", options.TimeoutSeconds);
            return new CliResult(false, null, $"CLI timed out after {options.TimeoutSeconds}s");
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw; // genuine caller cancellation — propagate
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to run Claude CLI");
            return new CliResult(false, null, $"Failed to start CLI: {ex.Message}");
        }
    }

    private string BuildArgs(string prompt)
    {
        var escaped = prompt.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var modelFlag = string.IsNullOrWhiteSpace(options.Model) ? "" : $" --model {options.Model}";
        return $"-p \"{escaped}\" --output-format json --max-turns {options.MaxTurns}{modelFlag}";
    }

    /// <summary>
    /// Collapses multi-line indented prompts to a single line.
    /// Prevents issues with embedded newlines in command-line arguments on Windows.
    /// </summary>
    internal static string CollapseWhitespace(string text)
    {
        return WhitespaceRegex().Replace(text.Trim(), " ");
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    /// <summary>
    /// Parses the JSON envelope from <c>--output-format json</c>.
    /// Expected shape: { "result": "...", "is_error": false, ... }
    /// </summary>
    internal static string? ParseCliOutput(string rawOutput)
    {
        if (string.IsNullOrWhiteSpace(rawOutput))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(rawOutput);
            var root = doc.RootElement;

            if (root.TryGetProperty("is_error", out var isErr) && isErr.GetBoolean())
                return null;

            if (root.TryGetProperty("result", out var result))
                return result.GetString();

            return null;
        }
        catch (JsonException)
        {
            // Not valid JSON envelope — return raw text as fallback
            return rawOutput;
        }
    }

    /// <summary>
    /// Extracts the error message from CLI JSON output when is_error is true.
    /// </summary>
    private static string? TryExtractJsonError(string rawOutput)
    {
        if (string.IsNullOrWhiteSpace(rawOutput)) return null;
        try
        {
            using var doc = JsonDocument.Parse(rawOutput);
            var root = doc.RootElement;
            if (root.TryGetProperty("is_error", out var isErr) && isErr.GetBoolean() &&
                root.TryGetProperty("result", out var result))
                return result.GetString();
        }
        catch (JsonException) { }
        return null;
    }

    private void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch { /* best effort */ }
    }
}
