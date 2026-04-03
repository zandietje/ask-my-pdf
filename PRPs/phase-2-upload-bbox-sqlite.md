# PRP: Phase 2 — PDF Upload + Bounding Box Extraction + SQLite

## Objective
Accept PDF uploads, extract word-level bounding boxes via PdfPig (for citation highlighting), and persist everything in SQLite — establishing the data foundation for the question pipeline in Phase 3.

## Scope
**In scope:**
- SQLite database initialization with schema for documents, files, and bounding boxes
- PdfPig bounding box extraction (word-level coordinates per page)
- Coordinate transformer for converting PdfPig coords → viewer-compatible percentages
- Document upload service orchestrating extract → store
- REST endpoints: upload, list, get, serve PDF file
- API DTOs for document responses
- DI wiring in Program.cs
- Unit tests for CoordinateTransformer and BoundingBoxExtractor

**Out of scope:**
- Claude API integration (Phase 3)
- Question/answer pipeline (Phase 3)
- SSE streaming (Phase 3)
- Frontend UI changes (Phase 4)
- PDF viewer (Phase 5)

## Prerequisites
- Phase 1 scaffold complete (all projects compile, models exist)
- .NET 8 SDK installed
- NuGet packages already declared: `PdfPig` 0.1.14, `Microsoft.Data.Sqlite` 8.0.11
- A test PDF file for validation

## Outputs
When done:
- `POST /api/documents/upload` accepts a PDF, returns document metadata
- `GET /api/documents` returns list of all uploaded documents
- `GET /api/documents/{id}` returns single document metadata
- `GET /api/documents/{id}/file` returns original PDF binary
- SQLite database auto-created on startup with `documents`, `stored_files`, `page_bounds` tables
- Word-level bounding boxes extracted and stored for every uploaded PDF
- CoordinateTransformer can convert cited text + page number → percentage-based highlight areas
- `dotnet build` passes, `dotnet test` passes (including new tests)

## Files to Create/Modify

### New Files
| File | Action | Description |
|------|--------|-------------|
| `src/AskMyPdf.Infrastructure/Data/SqliteDb.cs` | Create | SQLite database: init schema, CRUD for documents/files/bounds |
| `src/AskMyPdf.Infrastructure/Pdf/BoundingBoxExtractor.cs` | Create | PdfPig word extraction per page |
| `src/AskMyPdf.Infrastructure/Pdf/CoordinateTransformer.cs` | Create | Fuzzy text match + coord conversion to viewer percentages |
| `src/AskMyPdf.Core/Services/DocumentService.cs` | Create | Orchestrator: upload → extract bounds → save to SQLite |
| `src/AskMyPdf.Web/Endpoints/DocumentEndpoints.cs` | Create | 4 REST endpoints for document operations |
| `src/AskMyPdf.Web/Dtos/DocumentDto.cs` | Create | API response DTO for document metadata |
| `src/AskMyPdf.Web/Dtos/UploadResponse.cs` | Create | API response DTO for upload result |
| `tests/AskMyPdf.Tests/CoordinateTransformerTests.cs` | Create | Unit tests for coord conversion + text matching |
| `tests/AskMyPdf.Tests/BoundingBoxExtractorTests.cs` | Create | Unit tests for PdfPig word extraction |
| `tests/AskMyPdf.Tests/Fixtures/test-sample.pdf` | Create | Small test PDF for extraction tests |

### Modified Files
| File | Action | Description |
|------|--------|-------------|
| `src/AskMyPdf.Web/Program.cs` | Modify | Add DI registrations + endpoint mapping |
| `tests/AskMyPdf.Tests/AskMyPdf.Tests.csproj` | Modify | Add ProjectReference to Infrastructure, embed test PDF |

## NuGet Packages Needed
All already declared in Phase 1 — no new packages required.

| Package | Version | Project | Status |
|---------|---------|---------|--------|
| `PdfPig` | 0.1.14 | AskMyPdf.Infrastructure | Already in csproj |
| `Microsoft.Data.Sqlite` | 8.0.11 | AskMyPdf.Infrastructure | Already in csproj |
| `FluentAssertions` | 6.12.2 | AskMyPdf.Tests | Already in csproj |

## Implementation Steps

### Step 1: SQLite Database — `SqliteDb.cs`

Create `src/AskMyPdf.Infrastructure/Data/SqliteDb.cs`:

```csharp
namespace AskMyPdf.Infrastructure.Data;

using System.Text.Json;
using AskMyPdf.Core.Models;
using Microsoft.Data.Sqlite;

public class SqliteDb(string dbPath)
{
    private readonly string _connectionString = $"Data Source={dbPath}";

    public async Task InitializeAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS documents (
                id TEXT PRIMARY KEY,
                file_name TEXT NOT NULL,
                uploaded_at TEXT NOT NULL,
                page_count INTEGER NOT NULL,
                file_size INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS stored_files (
                document_id TEXT PRIMARY KEY REFERENCES documents(id),
                file_bytes BLOB NOT NULL
            );

            CREATE TABLE IF NOT EXISTS page_bounds (
                document_id TEXT NOT NULL REFERENCES documents(id),
                page_number INTEGER NOT NULL,
                page_width REAL NOT NULL,
                page_height REAL NOT NULL,
                words_json TEXT NOT NULL,
                PRIMARY KEY (document_id, page_number)
            );
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    // Methods needed:
    // SaveDocumentAsync(Document doc, byte[] fileBytes, List<PageBoundingData> bounds)
    // GetDocumentAsync(string id) -> Document?
    // GetAllDocumentsAsync() -> List<Document>
    // GetFileAsync(string documentId) -> byte[]?
    // GetPageBoundsAsync(string documentId) -> List<PageBoundingData>
}
```

**Schema design decisions:**
- `stored_files` is a separate table to avoid loading large blobs when listing documents
- `words_json` stores `List<WordBoundingBox>` as JSON — simple, no complex schema needed for bounding box data
- Composite PK on `page_bounds` (document_id, page_number) for efficient per-page lookups
- `uploaded_at` stored as ISO 8601 text (SQLite has no native datetime)

**Key implementation details:**
- Use `SqliteConnection` directly (no EF Core — KISS for a 3-day project)
- All methods create their own connection (connection-per-request pattern)
- Use `System.Text.Json` for words_json serialization/deserialization
- Use parameterized queries everywhere (no SQL injection)
- `SaveDocumentAsync` should use a transaction to atomically save doc + file + all page bounds
- Store `DateTime` as `doc.UploadedAt.ToString("O")` (ISO 8601 round-trip format)

### Step 2: Bounding Box Extractor — `BoundingBoxExtractor.cs`

Create `src/AskMyPdf.Infrastructure/Pdf/BoundingBoxExtractor.cs`:

```csharp
namespace AskMyPdf.Infrastructure.Pdf;

using AskMyPdf.Core.Models;
using UglyToad.PdfPig;

public class BoundingBoxExtractor
{
    public List<PageBoundingData> ExtractWordBounds(Stream pdfStream)
    {
        // Read stream to byte[] for PdfPig (requires seekable stream or byte[])
        using var ms = new MemoryStream();
        pdfStream.CopyTo(ms);
        var bytes = ms.ToArray();

        using var document = PdfDocument.Open(bytes);
        var pages = new List<PageBoundingData>();

        for (var i = 1; i <= document.NumberOfPages; i++)
        {
            var page = document.GetPage(i);
            var words = page.GetWords()
                .Select(w => new WordBoundingBox(
                    w.Text,
                    w.BoundingBox.Left,
                    w.BoundingBox.Bottom,
                    w.BoundingBox.Right,
                    w.BoundingBox.Top))
                .ToList();

            pages.Add(new PageBoundingData(
                i,
                page.Width,
                page.Height,
                words));
        }

        return pages;
    }
}
```

**Key details:**
- PdfPig's `PdfDocument.Open()` accepts `byte[]` — copy stream first
- `page.GetWords()` returns words with `BoundingBox` (Left, Bottom, Right, Top in PDF units)
- PDF units: origin at bottom-left, Y increases upward
- `page.Width` and `page.Height` are in the same coordinate system — essential for percentage conversion
- Page numbers from PdfPig are 1-indexed (matches Claude's `start_page_number`)

### Step 3: Coordinate Transformer — `CoordinateTransformer.cs`

Create `src/AskMyPdf.Infrastructure/Pdf/CoordinateTransformer.cs`:

```csharp
namespace AskMyPdf.Infrastructure.Pdf;

using AskMyPdf.Core.Models;

public class CoordinateTransformer
{
    public List<HighlightArea> ToHighlightAreas(
        string citedText,
        int pageNumber,
        List<PageBoundingData> pages)
    {
        // 1. Find the page data (pageNumber is 1-indexed)
        var page = pages.FirstOrDefault(p => p.PageNumber == pageNumber);
        if (page is null || page.Words.Count == 0)
            return [];

        // 2. Normalize cited text for matching
        var normalizedCited = Normalize(citedText);

        // 3. Find matching word range via sliding window
        var (startIdx, endIdx) = FindMatchingWords(normalizedCited, page.Words);
        if (startIdx < 0)
            return []; // No match — caller can fall back to page-level nav

        // 4. Get matched words
        var matchedWords = page.Words.Skip(startIdx).Take(endIdx - startIdx + 1).ToList();

        // 5. Group into line-level rectangles and convert to percentages
        return GroupIntoHighlightAreas(matchedWords, page, pageNumber);
    }
}
```

**Normalization logic:**
- Collapse multiple whitespace to single space
- Strip control characters (PdfPig sometimes extracts `\u0002` etc.)
- Trim leading/trailing whitespace
- Apply to both `citedText` and concatenated word text before comparison

**Sliding window matching:**
```
Build normalized string from consecutive words: "word1 word2 word3 ..."
Slide window over word sequence, checking if normalized cited text
is a substring of the concatenated window.
Use a widening window approach:
  1. Start with words that could match the beginning of cited text
  2. Expand until the concatenation contains the full cited text
  3. Return the word index range
```

**Important edge cases:**
- Cited text may span only part of a word (e.g., hyphenated words)
- Cited text may have different whitespace than PdfPig output
- Fall back to empty list (page-level navigation) if no match — don't crash

**Coordinate conversion formulas** (PdfPig → viewer percentages):
```
left_pct   = (word.Left / pageWidth) * 100
top_pct    = ((pageHeight - word.Top) / pageHeight) * 100
width_pct  = ((word.Right - word.Left) / pageWidth) * 100
height_pct = ((word.Top - word.Bottom) / pageHeight) * 100
```
- PdfPig: origin bottom-left, Y up. `Top > Bottom` for any word.
- Viewer: origin top-left, Y down. `top_pct` transforms Y axis.
- All values are percentages 0-100.

**Line grouping:**
- Words on the same line have similar `Top` values (within tolerance ~2 PDF units)
- Group consecutive matched words by line
- For each line group: create one `HighlightArea` that spans from leftmost word to rightmost word
- `PageIndex` = `pageNumber - 1` (viewer is 0-indexed, Claude is 1-indexed)

### Step 4: Document Service — `DocumentService.cs`

Create `src/AskMyPdf.Core/Services/DocumentService.cs`:

```csharp
namespace AskMyPdf.Core.Services;

using AskMyPdf.Core.Models;

public class DocumentService(
    Infrastructure.Pdf.BoundingBoxExtractor extractor,
    Infrastructure.Data.SqliteDb db)
{
    public async Task<Document> UploadAsync(Stream pdfStream, string fileName, long fileSize)
    {
        // 1. Read stream to byte[] (needed for both PdfPig and storage)
        using var ms = new MemoryStream();
        await pdfStream.CopyToAsync(ms);
        var bytes = ms.ToArray();

        // 2. Extract bounding boxes
        var pages = extractor.ExtractWordBounds(new MemoryStream(bytes));

        // 3. Create document record
        var doc = new Document(
            Id: Guid.NewGuid().ToString("N"),
            FileName: fileName,
            UploadedAt: DateTime.UtcNow,
            PageCount: pages.Count,
            FileSize: fileSize);

        // 4. Save atomically to SQLite
        await db.SaveDocumentAsync(doc, bytes, pages);

        return doc;
    }
}
```

**Note on project references:** `DocumentService` lives in Core but depends on Infrastructure types. This is a pragmatic decision — for a 3-day project, avoiding interfaces/abstractions unless solving a real problem (per CLAUDE.md hard rule #6). If this coupling bothers us later, we can extract interfaces, but YAGNI for now.

**Actually — better approach**: Move `DocumentService` to Infrastructure to avoid Core→Infrastructure dependency:

Create `src/AskMyPdf.Infrastructure/Services/DocumentService.cs` instead. This keeps the dependency direction clean: Web → Infrastructure → Core.

### Step 5: API DTOs

Create `src/AskMyPdf.Web/Dtos/DocumentDto.cs`:
```csharp
namespace AskMyPdf.Web.Dtos;

public record DocumentDto(
    string Id,
    string FileName,
    DateTime UploadedAt,
    int PageCount,
    long FileSize);
```

Create `src/AskMyPdf.Web/Dtos/UploadResponse.cs`:
```csharp
namespace AskMyPdf.Web.Dtos;

public record UploadResponse(
    string Id,
    string FileName,
    int PageCount,
    long FileSize);
```

### Step 6: Document Endpoints — `DocumentEndpoints.cs`

Create `src/AskMyPdf.Web/Endpoints/DocumentEndpoints.cs`:

```csharp
namespace AskMyPdf.Web.Endpoints;

public static class DocumentEndpoints
{
    public static void MapDocumentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/documents");

        // POST /api/documents/upload
        group.MapPost("/upload", async (IFormFile file, DocumentService svc) =>
        {
            // Validate: must be PDF, not empty, under 32MB
            if (file.Length == 0)
                return Results.BadRequest(new { error = "File is empty" });
            if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { error = "Only PDF files are accepted" });
            if (file.Length > 32 * 1024 * 1024)
                return Results.BadRequest(new { error = "File exceeds 32MB limit" });

            await using var stream = file.OpenReadStream();
            var doc = await svc.UploadAsync(stream, file.FileName, file.Length);

            return Results.Ok(new UploadResponse(doc.Id, doc.FileName, doc.PageCount, doc.FileSize));
        })
        .DisableAntiforgery(); // Required for IFormFile in Minimal API

        // GET /api/documents
        group.MapGet("/", async (SqliteDb db) =>
        {
            var docs = await db.GetAllDocumentsAsync();
            return Results.Ok(docs.Select(d =>
                new DocumentDto(d.Id, d.FileName, d.UploadedAt, d.PageCount, d.FileSize)));
        });

        // GET /api/documents/{id}
        group.MapGet("/{id}", async (string id, SqliteDb db) =>
        {
            var doc = await db.GetDocumentAsync(id);
            return doc is null
                ? Results.NotFound()
                : Results.Ok(new DocumentDto(doc.Id, doc.FileName, doc.UploadedAt, doc.PageCount, doc.FileSize));
        });

        // GET /api/documents/{id}/file
        group.MapGet("/{id}/file", async (string id, SqliteDb db) =>
        {
            var bytes = await db.GetFileAsync(id);
            return bytes is null
                ? Results.NotFound()
                : Results.File(bytes, "application/pdf");
        });
    }
}
```

**Key details:**
- `.DisableAntiforgery()` is required for `IFormFile` in .NET 8 Minimal APIs
- Input validation: PDF extension, non-empty, 32MB max (Claude API limit)
- Inject `SqliteDb` directly for read-only endpoints (no need to go through DocumentService)
- Return appropriate HTTP status codes: 200 for success, 400 for validation, 404 for missing

### Step 7: Wire DI in Program.cs

Modify `src/AskMyPdf.Web/Program.cs`:

```csharp
using AskMyPdf.Infrastructure.Data;
using AskMyPdf.Infrastructure.Pdf;
using AskMyPdf.Infrastructure.Services;
using AskMyPdf.Web.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// Infrastructure
var dbPath = builder.Configuration["Database:Path"] ?? "askmypdf.db";
builder.Services.AddSingleton(new SqliteDb(dbPath));
builder.Services.AddSingleton<BoundingBoxExtractor>();
builder.Services.AddScoped<DocumentService>();

var app = builder.Build();

// Initialize database
await app.Services.GetRequiredService<SqliteDb>().InitializeAsync();

// Endpoints
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapDocumentEndpoints();

app.Run();
```

**DI decisions:**
- `SqliteDb`: Singleton — manages its own connections, thread-safe with connection-per-call
- `BoundingBoxExtractor`: Singleton — stateless
- `DocumentService`: Scoped — orchestrates per-request work
- Database initialized eagerly at startup (creates tables if not exist)

### Step 8: Tests

**Test project modification** — add Infrastructure reference in `tests/AskMyPdf.Tests/AskMyPdf.Tests.csproj`:
```xml
<ProjectReference Include="..\..\src\AskMyPdf.Infrastructure\AskMyPdf.Infrastructure.csproj" />
```

Also embed a test PDF:
```xml
<ItemGroup>
  <None Update="Fixtures\test-sample.pdf">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```

**Create a test PDF:** Use a simple programmatic approach — either include a small real PDF in the Fixtures folder, or generate one in test setup. A real PDF is more reliable.

**CoordinateTransformerTests.cs** (`tests/AskMyPdf.Tests/CoordinateTransformerTests.cs`):

```csharp
// Test cases:
// 1. Known coordinates: given specific word bounding boxes, verify percentage output
// 2. Y-axis flip: PdfPig bottom-left → viewer top-left
// 3. Text matching: exact match finds correct words
// 4. Text matching: whitespace normalization (extra spaces, newlines)
// 5. No match: returns empty list (graceful fallback)
// 6. Line grouping: words on same line produce single highlight area
// 7. Multi-line: words across lines produce multiple highlight areas
// 8. PageIndex is 0-based (pageNumber 1 → pageIndex 0)
```

**BoundingBoxExtractorTests.cs** (`tests/AskMyPdf.Tests/BoundingBoxExtractorTests.cs`):

```csharp
// Test cases:
// 1. Extract from test PDF: returns correct page count
// 2. Each page has non-empty word list
// 3. Page dimensions are positive
// 4. Word bounding boxes have reasonable coordinates (Left < Right, Bottom < Top)
// 5. Word text is non-empty
```

### Step 9: Validate

Run full build and test suite:
```bash
dotnet build AskMyPdf.sln
dotnet test AskMyPdf.sln
```

Manual validation:
```bash
dotnet run --project src/AskMyPdf.Web/AskMyPdf.Web.csproj

# In another terminal:
curl -F "file=@test.pdf" http://localhost:5000/api/documents/upload
curl http://localhost:5000/api/documents
curl http://localhost:5000/api/documents/{id}/file --output downloaded.pdf
```

## Patterns to Follow

### Existing model pattern (from Phase 1)
```csharp
// File-scoped namespace, record type
namespace AskMyPdf.Core.Models;

public record Document(
    string Id,
    string FileName,
    DateTime UploadedAt,
    int PageCount,
    long FileSize);
```

### Primary constructor pattern (modern C#)
```csharp
namespace AskMyPdf.Infrastructure.Data;

public class SqliteDb(string dbPath)
{
    private readonly string _connectionString = $"Data Source={dbPath}";
    // ...
}
```

### Endpoint grouping pattern (Minimal API)
```csharp
public static class DocumentEndpoints
{
    public static void MapDocumentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/documents");
        group.MapPost("/upload", async (IFormFile file, DocumentService svc) => { ... });
        // ...
    }
}
```

### SQL with parameterized queries
```csharp
cmd.CommandText = "INSERT INTO documents (id, file_name) VALUES (@id, @name)";
cmd.Parameters.AddWithValue("@id", doc.Id);
cmd.Parameters.AddWithValue("@name", doc.FileName);
```

## Risks

| Risk | Mitigation |
|------|------------|
| PdfPig `GetWords()` returns empty for image-based PDFs | Assessment says "clean, machine-readable PDFs" — this is acceptable. Return empty word list gracefully. |
| PdfPig coordinate system varies by PDF rotation | Most PDFs are unrotated. Handle in CoordinateTransformer if encountered — check `page.Rotation` and adjust. |
| SQLite concurrent write contention | Singleton SqliteDb with connection-per-call handles this. SQLite's WAL mode could help if needed, but unlikely for single-user dev assessment. |
| Large PDFs (100+ pages) slow bounding box extraction | Validate page count on upload. PdfPig is fast for text extraction — 100 pages should process in <2 seconds. |
| `cited_text` whitespace doesn't match PdfPig word concatenation | Normalize both sides aggressively: collapse whitespace, strip control chars, case-insensitive matching. Fall back to page-level navigation (empty highlight areas) if no match. |
| `IFormFile` requires `.DisableAntiforgery()` in .NET 8 Minimal API | Already accounted for in the endpoint definition. Without it, upload will fail with 400. |

## Validation

- [ ] `dotnet build AskMyPdf.sln` — zero errors
- [ ] `dotnet test AskMyPdf.sln` — all tests pass (including new CoordinateTransformer and BoundingBoxExtractor tests)
- [ ] Upload a multi-page PDF via `POST /api/documents/upload` — returns 200 with id, fileName, pageCount, fileSize
- [ ] `GET /api/documents` — returns list containing the uploaded document
- [ ] `GET /api/documents/{id}` — returns the specific document metadata
- [ ] `GET /api/documents/{id}/file` — returns the original PDF binary (opens correctly in a viewer)
- [ ] SQLite database file (`askmypdf.db`) is created on startup
- [ ] Page bounds stored in SQLite: verify `page_bounds` table has rows for each page with non-empty `words_json`
- [ ] Upload validation: empty file → 400, non-PDF → 400, oversized → 400
- [ ] CoordinateTransformer: known input coordinates produce expected percentage outputs
- [ ] CoordinateTransformer: Y-axis correctly flipped (PdfPig bottom-left → viewer top-left)
- [ ] CoordinateTransformer: text matching handles whitespace normalization
- [ ] CoordinateTransformer: no match returns empty list (doesn't crash)
