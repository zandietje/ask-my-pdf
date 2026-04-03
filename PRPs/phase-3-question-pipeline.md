# PRP: Phase 3 — Question Pipeline + Claude Citations + SSE Streaming

## Objective
Send the full PDF to Claude's API with citations enabled, stream the answer back to the client via SSE with enriched citation events (including bounding box highlight coordinates) — the core feature that makes this application work.

## Scope
**In scope:**
- `ClaudeService` — sends base64 PDF to Claude API with citations enabled, prompt caching, streaming
- `QuestionService` — orchestrates Claude API call + citation-to-bounding-box matching + SSE event emission
- `QuestionEndpoints` — SSE streaming endpoint (`POST /api/questions`)
- `QuestionRequest` DTO
- DI wiring for new services in `Program.cs`
- Grounding system prompt
- Integration test with real Claude API (manual validation)

**Out of scope:**
- Frontend UI (Phase 4)
- PDF viewer / highlighting (Phase 5)
- Conversation history / multi-turn chat (not in requirements)
- Error handling polish (Phase 6)

## Prerequisites
- Phase 2 complete: PDF upload, bounding box extraction, SQLite storage all working
- Anthropic API key configured via user secrets (`dotnet user-secrets set "Anthropic:ApiKey" "sk-ant-..."`)
- `Anthropic` NuGet package 12.11.0 already in `AskMyPdf.Infrastructure.csproj`
- At least one PDF uploaded via `POST /api/documents/upload` for testing
- Existing infrastructure: `SqliteDb.GetFileAsync()`, `SqliteDb.GetPageBoundsAsync()`, `CoordinateTransformer.ToHighlightAreas()`

## Outputs
When done:
- `POST /api/questions` accepts `{ question, documentId }` and streams SSE events
- SSE event types: `text-delta` (streaming text), `citation` (with highlight coordinates), `done`
- Citations include page number, cited text, and percentage-based highlight areas from bounding box matching
- "I don't know" responses handled gracefully (no citations, clear message)
- Prompt caching enabled (90% cost reduction on repeat questions for same document)
- `dotnet build AskMyPdf.sln` passes
- Manual end-to-end test: upload PDF → ask question → receive streamed answer with citations

## Files to Create/Modify

### New Files
| File | Action | Description |
|------|--------|-------------|
| `src/AskMyPdf.Infrastructure/Ai/ClaudeService.cs` | Create | Sends base64 PDF to Claude API with citations enabled, streams response |
| `src/AskMyPdf.Infrastructure/Services/QuestionService.cs` | Create | Orchestrates: load data → call Claude → match citations → yield SSE events |
| `src/AskMyPdf.Web/Endpoints/QuestionEndpoints.cs` | Create | SSE streaming endpoint for `POST /api/questions` |
| `src/AskMyPdf.Web/Dtos/QuestionRequest.cs` | Create | Request DTO: `{ Question, DocumentId }` |

### Modified Files
| File | Action | Description |
|------|--------|-------------|
| `src/AskMyPdf.Web/Program.cs` | Modify | Add DI registrations for `ClaudeService`, `QuestionService` + map question endpoints |

## NuGet Packages Needed
All already declared — no new packages required.

| Package | Version | Project | Status |
|---------|---------|---------|--------|
| `Anthropic` | 12.11.0 | AskMyPdf.Infrastructure | Already in csproj |

## Implementation Steps

### Step 1: ClaudeService — `src/AskMyPdf.Infrastructure/Ai/ClaudeService.cs`

This is the **most critical file** in the entire project. It interfaces with the Claude API.

```csharp
namespace AskMyPdf.Infrastructure.Ai;

using Anthropic;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using AskMyPdf.Core.Models;
using Microsoft.Extensions.Logging;

public class ClaudeService(AnthropicClient client, ILogger<ClaudeService> logger)
{
    private const string GroundingPrompt = """
        You are a document Q&A assistant. Answer the user's question based ONLY on the provided document.

        Rules:
        1. Only use information explicitly stated in the document
        2. If the document does not contain the answer, say: "I could not find an answer to this question in the provided document."
        3. Quote relevant text to support your answer
        4. If multiple sections are relevant, reference all of them
        5. Never make up or infer information beyond what is explicitly stated
        6. Be concise and direct
        """;

    public async IAsyncEnumerable<AnswerStreamEvent> StreamAnswerAsync(
        string question,
        byte[] pdfBytes,
        string fileName)
    {
        var pdfBase64 = Convert.ToBase64String(pdfBytes);

        var parameters = new MessageCreateParams
        {
            Model = "claude-sonnet-4-5-20250514",
            MaxTokens = 4096,
            System = new SystemPrompt(GroundingPrompt),
            Messages =
            [
                new()
                {
                    Role = Role.User,
                    Content =
                    [
                        new DocumentBlockParam(new Base64PdfSource(pdfBase64))
                        {
                            Title = fileName,
                            Citations = new CitationsConfigParam { Enabled = true },
                            CacheControl = new CacheControlEphemeral(),
                        },
                        new TextBlockParam { Text = question },
                    ],
                },
            ],
        };

        // Stream the response
        await foreach (var evt in client.Messages.CreateStreamingAsync(parameters))
        {
            // Handle text delta events
            if (evt is ContentBlockDelta { Delta: TextDelta textDelta })
            {
                yield return new AnswerStreamEvent.TextDelta(textDelta.Text);
            }
            // Handle citation events
            else if (evt is ContentBlockDelta { Delta: CitationsDelta citationDelta })
            {
                if (citationDelta.Citation is PageLocationCitation pageCitation)
                {
                    yield return new AnswerStreamEvent.CitationReceived(
                        new Citation(
                            DocumentId: "", // Filled by QuestionService
                            DocumentName: fileName,
                            PageNumber: pageCitation.StartPageNumber,
                            CitedText: pageCitation.CitedText,
                            HighlightAreas: [])); // Filled by QuestionService
                }
            }
        }

        yield return new AnswerStreamEvent.Done();
    }
}
```

**Critical SDK exploration notes:**
- The Anthropic C# SDK (12.11.0) is in active development. The exact type names for streaming, citations, and document blocks may differ from the examples above.
- **If the typed API doesn't expose the right types**, use these fallback strategies:
  1. Check the SDK source/README for the actual type names (e.g., `MessageStreamEvent`, `StreamEvent`, etc.)
  2. If document blocks with citations aren't exposed as typed classes, construct the request JSON manually using `HttpClient`
  3. The raw HTTP API format is documented at https://docs.anthropic.com/en/docs/build-with-claude/citations
- **Key things to discover during implementation:**
  - How does the SDK represent the streaming response? (`IAsyncEnumerable<T>`? event callbacks?)
  - What are the actual type names for `DocumentBlockParam`, `Base64PdfSource`, `CitationsConfigParam`?
  - How are citations surfaced in the streaming response? (dedicated event type? part of content block?)
  - How is `CacheControl` set on a content block?
- **Budget 1-2 hours for SDK exploration.** Start by reading the SDK's README/samples, then write a minimal test script.
- The grounding prompt goes in the `System` parameter, NOT in the user message.

**Prompt caching:**
- `CacheControl = new CacheControlEphemeral()` on the document block tells Claude to cache the PDF processing
- First question: full cost (~$0.23 for a 30-page PDF)
- Subsequent questions within 5 min: 90% cheaper (~$0.02 each)
- The cache TTL refreshes on each hit — stays cached as long as questions keep coming

### Step 2: QuestionService — `src/AskMyPdf.Infrastructure/Services/QuestionService.cs`

Orchestrates the full pipeline: load data → call Claude → enrich citations with bounding boxes.

```csharp
namespace AskMyPdf.Infrastructure.Services;

using AskMyPdf.Core.Models;
using AskMyPdf.Infrastructure.Ai;
using AskMyPdf.Infrastructure.Data;
using AskMyPdf.Infrastructure.Pdf;
using Microsoft.Extensions.Logging;

public class QuestionService(
    ClaudeService claude,
    SqliteDb db,
    CoordinateTransformer transformer,
    ILogger<QuestionService> logger)
{
    public async IAsyncEnumerable<AnswerStreamEvent> StreamAnswerAsync(
        string question,
        string documentId)
    {
        // 1. Load document metadata
        var doc = await db.GetDocumentAsync(documentId);
        if (doc is null)
        {
            yield return new AnswerStreamEvent.TextDelta("Document not found.");
            yield return new AnswerStreamEvent.Done();
            yield break;
        }

        // 2. Load PDF bytes
        var pdfBytes = await db.GetFileAsync(documentId);
        if (pdfBytes is null)
        {
            yield return new AnswerStreamEvent.TextDelta("Document file not found.");
            yield return new AnswerStreamEvent.Done();
            yield break;
        }

        // 3. Load page bounds for citation matching
        var pageBounds = await db.GetPageBoundsAsync(documentId);

        // 4. Stream from Claude and enrich citations
        await foreach (var evt in claude.StreamAnswerAsync(question, pdfBytes, doc.FileName))
        {
            switch (evt)
            {
                case AnswerStreamEvent.TextDelta:
                    yield return evt;
                    break;

                case AnswerStreamEvent.CitationReceived { Citation: var citation }:
                    // Enrich citation with bounding box highlight areas
                    var highlightAreas = transformer.ToHighlightAreas(
                        citation.CitedText,
                        citation.PageNumber,
                        pageBounds);

                    yield return new AnswerStreamEvent.CitationReceived(
                        citation with
                        {
                            DocumentId = documentId,
                            HighlightAreas = highlightAreas,
                        });
                    break;

                case AnswerStreamEvent.Done:
                    yield return evt;
                    break;
            }
        }
    }
}
```

**Key design decisions:**
- `QuestionService` lives in Infrastructure (not Core) because it depends on `ClaudeService` and `SqliteDb` — same pattern as `DocumentService`
- Load `pageBounds` eagerly before streaming starts — they're needed synchronously for each citation
- Use `citation with { ... }` record copy to fill in `DocumentId` and `HighlightAreas`
- If `ToHighlightAreas` returns empty (no text match), the citation still works — frontend can navigate to the page without highlighting
- Document-not-found is handled gracefully by returning a text message + done event

### Step 3: QuestionRequest DTO — `src/AskMyPdf.Web/Dtos/QuestionRequest.cs`

```csharp
namespace AskMyPdf.Web.Dtos;

public record QuestionRequest(string Question, string DocumentId);
```

### Step 4: QuestionEndpoints — `src/AskMyPdf.Web/Endpoints/QuestionEndpoints.cs`

SSE streaming endpoint following the same extension method pattern as `DocumentEndpoints`.

```csharp
namespace AskMyPdf.Web.Endpoints;

using System.Text.Json;
using AskMyPdf.Core.Models;
using AskMyPdf.Infrastructure.Services;
using AskMyPdf.Web.Dtos;

public static class QuestionEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static void MapQuestionEndpoints(this WebApplication app)
    {
        app.MapPost("/api/questions", async (QuestionRequest req, QuestionService svc, HttpContext ctx) =>
        {
            // Validate
            if (string.IsNullOrWhiteSpace(req.Question))
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsJsonAsync(new { error = "Question is required" });
                return;
            }
            if (string.IsNullOrWhiteSpace(req.DocumentId))
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsJsonAsync(new { error = "DocumentId is required" });
                return;
            }

            // Set SSE headers
            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.Connection = "keep-alive";

            await foreach (var evt in svc.StreamAnswerAsync(req.Question, req.DocumentId))
            {
                var (eventType, data) = evt switch
                {
                    AnswerStreamEvent.TextDelta td => ("text-delta", JsonSerializer.Serialize(
                        new { text = td.Text }, JsonOptions)),
                    AnswerStreamEvent.CitationReceived cr => ("citation", JsonSerializer.Serialize(
                        new
                        {
                            documentId = cr.Citation.DocumentId,
                            documentName = cr.Citation.DocumentName,
                            pageNumber = cr.Citation.PageNumber,
                            citedText = cr.Citation.CitedText,
                            highlightAreas = cr.Citation.HighlightAreas.Select(a => new
                            {
                                pageIndex = a.PageIndex,
                                left = a.Left,
                                top = a.Top,
                                width = a.Width,
                                height = a.Height,
                            }),
                        }, JsonOptions)),
                    AnswerStreamEvent.Done => ("done", "{}"),
                    _ => ("unknown", "{}"),
                };

                await ctx.Response.WriteAsync($"event: {eventType}\ndata: {data}\n\n");
                await ctx.Response.Body.FlushAsync();
            }
        });
    }
}
```

**SSE format details:**
- Each event: `event: <type>\ndata: <json>\n\n`
- Event types: `text-delta`, `citation`, `done`
- `text-delta` data: `{ "text": "chunk of answer text" }`
- `citation` data: `{ "documentId": "...", "documentName": "...", "pageNumber": 3, "citedText": "...", "highlightAreas": [...] }`
- `done` data: `{}`
- `Content-Type: text/event-stream` required for browser EventSource compatibility
- `Cache-Control: no-cache` prevents buffering
- `Connection: keep-alive` keeps the stream open
- Flush after each event to ensure immediate delivery
- The endpoint uses `HttpContext` directly (not `Results.*`) because SSE requires manual response writing

**Why `HttpContext` instead of returning `IResult`:**
SSE requires streaming writes to the response body. `IResult` returns are designed for single-shot responses. We need to set headers, then write multiple events over time, flushing after each one. `HttpContext` gives us direct access to `Response.WriteAsync()` and `Response.Body.FlushAsync()`.

### Step 5: Wire DI in Program.cs

Modify `src/AskMyPdf.Web/Program.cs` to add:

```csharp
// Add these using statements
using AskMyPdf.Infrastructure.Ai;

// Add these DI registrations (after existing ones)
var apiKey = builder.Configuration["Anthropic:ApiKey"]
    ?? throw new InvalidOperationException("Anthropic:ApiKey is required. Set via user-secrets or appsettings.json.");
builder.Services.AddSingleton(new AnthropicClient(apiKey));
builder.Services.AddSingleton<ClaudeService>();
builder.Services.AddScoped<QuestionService>();

// Add this endpoint mapping (after existing ones)
app.MapQuestionEndpoints();
```

**DI decisions:**
- `AnthropicClient`: Singleton — the SDK client is designed to be reused (manages HTTP connections internally)
- `ClaudeService`: Singleton — stateless, wraps the client
- `QuestionService`: Scoped — orchestrates per-request work, depends on `SqliteDb` (singleton) and `CoordinateTransformer` (singleton)
- API key validation at startup (fail fast) — don't wait until first question to discover it's missing

**Configuration:**
- API key via `appsettings.json` (already has `Anthropic:ApiKey` placeholder) or user secrets:
  ```bash
  cd src/AskMyPdf.Web
  dotnet user-secrets init  # If not already done
  dotnet user-secrets set "Anthropic:ApiKey" "sk-ant-api03-..."
  ```

### Step 6: CORS (if needed for dev)

If the Vite dev server at `http://localhost:5173` can't reach `http://localhost:5000`, add CORS to `Program.cs`:

```csharp
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

// After app is built:
app.UseCors();
```

**Note:** The Vite proxy config in `vite.config.ts` already proxies `/api/*` to `http://localhost:5000`, so CORS may not be needed during development. Only add if requests fail with CORS errors. In production, .NET serves the built React files directly (same origin), so CORS is not needed.

## Patterns to Follow

### Service pattern (from `DocumentService`)
```csharp
namespace AskMyPdf.Infrastructure.Services;

public class DocumentService(BoundingBoxExtractor extractor, SqliteDb db)
{
    public async Task<Document> UploadAsync(Stream pdfStream, string fileName, long fileSize)
    {
        // Primary constructor injection, async methods
    }
}
```

### Endpoint extension method pattern (from `DocumentEndpoints`)
```csharp
namespace AskMyPdf.Web.Endpoints;

public static class QuestionEndpoints
{
    public static void MapQuestionEndpoints(this WebApplication app)
    {
        app.MapPost("/api/questions", async (...) => { ... });
    }
}
```

### Record DTO pattern (from `DocumentDto`)
```csharp
namespace AskMyPdf.Web.Dtos;

public record QuestionRequest(string Question, string DocumentId);
```

### Model pattern (from `AnswerStreamEvent` — already exists)
```csharp
public abstract record AnswerStreamEvent
{
    public record TextDelta(string Text) : AnswerStreamEvent;
    public record CitationReceived(Citation Citation) : AnswerStreamEvent;
    public record Done : AnswerStreamEvent;
}
```

## Risks

| Risk | Mitigation |
|------|------------|
| **Anthropic C# SDK type mismatch** — typed API may not expose `DocumentBlockParam`, `CitationsConfigParam`, or streaming citation events as expected | Budget 1-2 hrs for SDK exploration. Inspect SDK source code (GitHub: tryAGI/Anthropic). Fallback: use raw `HttpClient` + manual JSON for the request, parse SSE stream manually. The HTTP API format is well-documented. |
| **Streaming API differences** — SDK may use callbacks/events instead of `IAsyncEnumerable` | Adapt to whatever the SDK provides. If it uses `event` handlers, wrap in a `Channel<T>` to convert to `IAsyncEnumerable`. If it returns a `Task<Message>` (non-streaming), fall back to non-streaming response and emit all events at once. |
| **Citation types in stream** — citations may arrive as part of content blocks, not as separate streaming events | Inspect the full `Message` response after streaming completes. Citations may be in `Content[].Citations[]` on the final message rather than streamed incrementally. If so, collect all text first, then emit citations at the end. |
| **`cited_text` whitespace mismatch** — Claude's extracted text may differ from PdfPig's word extraction | Already mitigated by `CoordinateTransformer.Normalize()` (collapse whitespace, strip control chars, case-insensitive). If no bounding box match found, citation still works — just no highlighting (page-level navigation only). |
| **Large PDF timeout** — 100-page PDF may exceed API timeout | Claude's context window supports up to 100 pages. Default timeout should be sufficient (streaming starts quickly). If needed, set a custom `HttpClient` timeout. |
| **API key missing at startup** — forgetting to configure user secrets | Fail fast with clear error message in `Program.cs`: `throw new InvalidOperationException("Anthropic:ApiKey is required")`. Make the error message actionable. |
| **SSE response buffering** — .NET may buffer the response, breaking real-time streaming | Flush after each event write. Ensure no response compression middleware is active. Set `Response.Headers.ContentEncoding` to empty if needed. |

## Validation

- [ ] `dotnet build AskMyPdf.sln` — zero errors
- [ ] `dotnet run --project src/AskMyPdf.Web/AskMyPdf.Web.csproj` — starts without error (API key configured)
- [ ] Upload a test PDF via `POST /api/documents/upload`
- [ ] `curl -X POST http://localhost:5000/api/questions -H "Content-Type: application/json" -d '{"question":"What is this document about?","documentId":"<id>"}' -N` — streams SSE events
- [ ] SSE stream contains `event: text-delta` events with answer text chunks
- [ ] SSE stream contains `event: citation` events with `pageNumber`, `citedText`, and `highlightAreas`
- [ ] SSE stream ends with `event: done`
- [ ] Citation `highlightAreas` contain valid percentage-based coordinates (0-100 range)
- [ ] Ask an unrelated question → response says "I could not find an answer" with no citations
- [ ] Ask a second question about the same document → response is faster (prompt cache hit — check logs)
- [ ] Missing `documentId` → 400 error with message
- [ ] Missing `question` → 400 error with message
- [ ] Non-existent `documentId` → graceful "Document not found" text event
