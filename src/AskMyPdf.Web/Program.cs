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

var app = builder.Build();

// Initialize database
await app.Services.GetRequiredService<SqliteDb>().InitializeAsync();

// Endpoints
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapDocumentEndpoints();

app.Run();
