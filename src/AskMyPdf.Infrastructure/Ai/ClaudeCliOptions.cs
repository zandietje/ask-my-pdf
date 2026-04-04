namespace AskMyPdf.Infrastructure.Ai;

/// <summary>Configuration for the Claude Code CLI engine.</summary>
public record ClaudeCliOptions(
    string BinaryPath = "claude",
    int TimeoutSeconds = 120,
    int MaxTurns = 5);
