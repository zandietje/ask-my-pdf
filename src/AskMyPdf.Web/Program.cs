var builder = WebApplication.CreateBuilder(args);

// TODO: Register services (Phase 2-3)

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

// TODO: Map endpoints (Phase 2-3)

app.Run();
