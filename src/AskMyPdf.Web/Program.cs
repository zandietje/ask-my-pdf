using Anthropic;
using AskMyPdf.Infrastructure.Ai;
using AskMyPdf.Infrastructure.Data;
using AskMyPdf.Infrastructure.Pdf;
using AskMyPdf.Infrastructure.Services;
using AskMyPdf.Web.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// Infrastructure
var dbPath = builder.Configuration["Database:Path"] ?? "askmypdf.db";
builder.Services.AddSingleton(new SqliteDb(dbPath));
builder.Services.AddSingleton<BoundingBoxExtractor>();
builder.Services.AddSingleton<CoordinateTransformer>();
builder.Services.AddScoped<DocumentService>();

// AI
var apiKey = builder.Configuration["Anthropic:ApiKey"];
if (string.IsNullOrWhiteSpace(apiKey))
    throw new InvalidOperationException(
        "Anthropic:ApiKey is required. Set via user-secrets: dotnet user-secrets set \"Anthropic:ApiKey\" \"sk-ant-...\"");
builder.Services.AddSingleton(new AnthropicClient(new Anthropic.Core.ClientOptions { ApiKey = apiKey }));
builder.Services.AddSingleton<ClaudeService>();
builder.Services.AddScoped<QuestionService>();

var app = builder.Build();

// Initialize database
await app.Services.GetRequiredService<SqliteDb>().InitializeAsync();

// Endpoints
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapDocumentEndpoints();
app.MapQuestionEndpoints();

app.Run();
