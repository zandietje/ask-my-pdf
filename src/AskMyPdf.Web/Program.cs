using Anthropic;
using AskMyPdf.Core.Services;
using AskMyPdf.Infrastructure.Ai;
using AskMyPdf.Infrastructure.Data;
using AskMyPdf.Infrastructure.Pdf;
using AskMyPdf.Infrastructure.Services;
using AskMyPdf.Web.Endpoints;
using Microsoft.AspNetCore.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

// Infrastructure — database
var dbPath = builder.Configuration["Database:Path"] ?? "askmypdf.db";
var dbFactory = new DbConnectionFactory(dbPath);
builder.Services.AddSingleton(dbFactory);
builder.Services.AddSingleton<DocumentRepository>();
builder.Services.AddSingleton<ChunkRepository>();

builder.Services.AddSingleton<BoundingBoxExtractor>();
builder.Services.AddSingleton<CoordinateTransformer>();
builder.Services.AddScoped<DocumentService>();

// AI — shared dependencies
var apiKey = builder.Configuration["Anthropic:ApiKey"];
if (string.IsNullOrWhiteSpace(apiKey))
    throw new InvalidOperationException(
        "Anthropic:ApiKey is required. Set via user-secrets: dotnet user-secrets set \"Anthropic:ApiKey\" \"sk-ant-...\"");
builder.Services.AddSingleton(new AnthropicClient(new Anthropic.Core.ClientOptions { ApiKey = apiKey }));
builder.Services.AddSingleton(new ClaudeServiceOptions(
    AnswerModel: builder.Configuration["Anthropic:AnswerModel"] ?? "claude-sonnet-4-20250514",
    FocusModel: builder.Configuration["Anthropic:FocusModel"] ?? "claude-haiku-4-5-20251001"));
builder.Services.AddSingleton<ClaudeService>();
builder.Services.AddSingleton<ICitationFocuser>(sp => sp.GetRequiredService<ClaudeService>());

// Embeddings — OpenAI (optional, enables hybrid vector+FTS5 retrieval)
builder.Services.AddHttpClient<EmbeddingService>();
builder.Services.AddSingleton(new EmbeddingOptions(
    ApiKey: builder.Configuration["OpenAI:ApiKey"],
    Model: builder.Configuration["OpenAI:EmbeddingModel"] ?? "text-embedding-3-small",
    Dimensions: builder.Configuration.GetValue("OpenAI:Dimensions", 1536)));
builder.Services.AddSingleton<EmbeddingService>();

// Engine registration order determines UI button order: Quick → Full → Deep

// Engine 1 — RAG (Quick): hybrid FTS5 + vector retrieval, fastest responses
var ragEnabled = builder.Configuration.GetValue<bool>("Rag:Enabled");
if (ragEnabled)
{
    builder.Services.AddSingleton(new RagEngineOptions(
        Model: builder.Configuration["Rag:Model"] ?? "claude-sonnet-4-20250514",
        TopK: builder.Configuration.GetValue("Rag:TopK", 8)));
    builder.Services.AddSingleton<IAnswerEngine, RagAnswerEngine>();
}

// Engine 2 — Anthropic API (Full): sends entire PDF with native citations
builder.Services.AddSingleton<IAnswerEngine, AnthropicAnswerEngine>();

// Engine 3 — Claude Code CLI (Deep): thorough multi-pass analysis
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

builder.Services.AddSingleton<DocumentChunker>();
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
await app.Services.GetRequiredService<DbConnectionFactory>().InitializeAsync();

// Endpoints
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapGet("/api/engines", (IEnumerable<IAnswerEngine> engines) =>
    Results.Ok(engines.Select(e => new { key = e.Key, name = e.DisplayName })));
app.MapDocumentEndpoints();
app.MapQuestionEndpoints();

// SPA fallback: non-API, non-file routes → index.html
app.MapFallbackToFile("index.html");

app.Run();
