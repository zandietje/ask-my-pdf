using Anthropic;
using AskMyPdf.Core.Services;
using AskMyPdf.Infrastructure.Ai;
using AskMyPdf.Infrastructure.Data;
using AskMyPdf.Infrastructure.Pdf;
using AskMyPdf.Infrastructure.Services;
using AskMyPdf.Web.Endpoints;
using Microsoft.AspNetCore.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

// Infrastructure
var dbPath = builder.Configuration["Database:Path"] ?? "askmypdf.db";
builder.Services.AddSingleton(new SqliteDb(dbPath));
builder.Services.AddSingleton<BoundingBoxExtractor>();
builder.Services.AddSingleton<CoordinateTransformer>();
builder.Services.AddScoped<DocumentService>();

// AI — Anthropic API engine (Engine A)
var apiKey = builder.Configuration["Anthropic:ApiKey"];
if (string.IsNullOrWhiteSpace(apiKey))
    throw new InvalidOperationException(
        "Anthropic:ApiKey is required. Set via user-secrets: dotnet user-secrets set \"Anthropic:ApiKey\" \"sk-ant-...\"");
builder.Services.AddSingleton(new AnthropicClient(new Anthropic.Core.ClientOptions { ApiKey = apiKey }));
builder.Services.AddSingleton(new ClaudeServiceOptions(
    AnswerModel: builder.Configuration["Anthropic:AnswerModel"] ?? "claude-sonnet-4-20250514",
    FocusModel: builder.Configuration["Anthropic:FocusModel"] ?? "claude-haiku-4-5-20251001"));
builder.Services.AddSingleton<ClaudeService>();
builder.Services.AddSingleton<IAnswerEngine, AnthropicAnswerEngine>();

// AI — Claude Code CLI engine (Engine B)
var cliEnabled = builder.Configuration.GetValue<bool>("ClaudeCli:Enabled");
if (cliEnabled)
{
    builder.Services.AddSingleton(new ClaudeCliOptions(
        BinaryPath: builder.Configuration["ClaudeCli:BinaryPath"] ?? "claude",
        TimeoutSeconds: builder.Configuration.GetValue("ClaudeCli:TimeoutSeconds", 120),
        MaxTurns: builder.Configuration.GetValue("ClaudeCli:MaxTurns", 5)));
    builder.Services.AddSingleton<ClaudeCliRunner>();
    builder.Services.AddSingleton<IAnswerEngine, ClaudeCliEngine>();
}

builder.Services.AddScoped<QuestionService>();

var app = builder.Build();

// Global exception handler — structured JSON errors, not stack traces
app.UseExceptionHandler(exApp =>
{
    exApp.Run(async context =>
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        var feature = context.Features.Get<IExceptionHandlerFeature>();
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogError(feature?.Error, "Unhandled exception");
        await context.Response.WriteAsJsonAsync(new { error = "An unexpected error occurred. Please try again." });
    });
});

// Static files for production (serves built React app from wwwroot/)
app.UseDefaultFiles();
app.UseStaticFiles();

// Initialize database
await app.Services.GetRequiredService<SqliteDb>().InitializeAsync();

// Endpoints
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapGet("/api/engines", (IEnumerable<IAnswerEngine> engines) =>
    Results.Ok(engines.Select(e => new { key = e.Key, name = e.DisplayName })));
app.MapDocumentEndpoints();
app.MapQuestionEndpoints();

// SPA fallback: non-API, non-file routes → index.html
app.MapFallbackToFile("index.html");

app.Run();
