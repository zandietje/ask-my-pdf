# RAG Engine — Implementation Plan

Engine C: retrieval-augmented generation as a third `IAnswerEngine` behind the same abstraction used by Anthropic API and Claude CLI.

---

## 1. Interface Change

### `IAnswerEngine.cs` — add `documentId` parameter

```csharp
// Before
IAsyncEnumerable<AnswerStreamEvent> StreamRawAnswerAsync(
    string question, byte[] pdfBytes, string fileName, CancellationToken ct = default);

// After
IAsyncEnumerable<AnswerStreamEvent> StreamRawAnswerAsync(
    string question, byte[] pdfBytes, string fileName, string documentId,
    CancellationToken ct = default);
```

Both existing engines ignore `documentId`. The RAG engine uses it to look up pre-indexed chunks.

### Update call site in `QuestionService.cs`

```csharp
// Line 54 — add documentId argument
await foreach (var evt in engine.StreamRawAnswerAsync(question, pdfBytes, doc.FileName, documentId, ct))
```

### Update `AnthropicAnswerEngine.cs`

```csharp
public async IAsyncEnumerable<AnswerStreamEvent> StreamRawAnswerAsync(
    string question, byte[] pdfBytes, string fileName, string documentId,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    // documentId unused — this engine sends the full PDF
    await foreach (var evt in claude.StreamAnswerAsync(question, pdfBytes, fileName, ct))
        yield return evt;
}
```

### Update `ClaudeCliEngine.cs`

Same signature change, `documentId` unused.

---

## 2. Database Schema

### New table: `document_chunks`

Add to `SqliteDb.InitializeAsync()`:

```sql
CREATE TABLE IF NOT EXISTS document_chunks (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    document_id TEXT NOT NULL REFERENCES documents(id),
    page_number INTEGER NOT NULL,
    chunk_index INTEGER NOT NULL,
    chunk_text TEXT NOT NULL,
    UNIQUE(document_id, chunk_index)
);
```

### FTS5 virtual table for full-text search

```sql
CREATE VIRTUAL TABLE IF NOT EXISTS document_chunks_fts USING fts5(
    chunk_text,
    content='document_chunks',
    content_rowid='id',
    tokenize='porter unicode61'
);
```

`porter` applies stemming (e.g. "running" matches "run"). `unicode61` handles accented characters.

### FTS5 sync triggers

FTS5 `content=` tables don't auto-sync. Add triggers to keep them in lockstep:

```sql
CREATE TRIGGER IF NOT EXISTS chunks_ai AFTER INSERT ON document_chunks BEGIN
    INSERT INTO document_chunks_fts(rowid, chunk_text) VALUES (new.id, new.chunk_text);
END;

CREATE TRIGGER IF NOT EXISTS chunks_ad AFTER DELETE ON document_chunks BEGIN
    INSERT INTO document_chunks_fts(document_chunks_fts, rowid, chunk_text)
    VALUES('delete', old.id, old.chunk_text);
END;
```

No update trigger needed — chunks are immutable (delete + re-insert on re-upload).

### Delete cascade

Extend `SqliteDb.DeleteDocumentAsync` to also delete from `document_chunks` (before `page_bounds`). The FTS5 trigger handles the virtual table automatically.

```csharp
foreach (var table in new[] { "document_chunks", "page_bounds", "stored_files" })
```

---

## 3. New `SqliteDb` Methods

### `SaveChunksAsync`

```csharp
public async Task SaveChunksAsync(string documentId, List<DocumentChunk> chunks)
{
    await using var conn = new SqliteConnection(_connectionString);
    await conn.OpenAsync();
    await using var tx = await conn.BeginTransactionAsync();

    foreach (var chunk in chunks)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = (SqliteTransaction)tx;
        cmd.CommandText = """
            INSERT INTO document_chunks (document_id, page_number, chunk_index, chunk_text)
            VALUES (@docId, @page, @idx, @text)
            """;
        cmd.Parameters.AddWithValue("@docId", documentId);
        cmd.Parameters.AddWithValue("@page", chunk.PageNumber);
        cmd.Parameters.AddWithValue("@idx", chunk.ChunkIndex);
        cmd.Parameters.AddWithValue("@text", chunk.ChunkText);
        await cmd.ExecuteNonQueryAsync();
    }

    await tx.CommitAsync();
}
```

### `SearchChunksAsync`

```csharp
public async Task<List<DocumentChunk>> SearchChunksAsync(string documentId, string query, int topK = 10)
{
    await using var conn = new SqliteConnection(_connectionString);
    await conn.OpenAsync();

    // Sanitize query for FTS5: remove special characters that FTS5 interprets as operators
    var sanitized = SanitizeFtsQuery(query);

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = """
        SELECT c.document_id, c.page_number, c.chunk_index, c.chunk_text
        FROM document_chunks c
        JOIN document_chunks_fts f ON f.rowid = c.id
        WHERE c.document_id = @docId
          AND document_chunks_fts MATCH @query
        ORDER BY bm25(document_chunks_fts)
        LIMIT @topK
        """;
    cmd.Parameters.AddWithValue("@docId", documentId);
    cmd.Parameters.AddWithValue("@query", sanitized);
    cmd.Parameters.AddWithValue("@topK", topK);

    var results = new List<DocumentChunk>();
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        results.Add(new DocumentChunk(
            reader.GetString(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetString(3)));
    }

    return results;
}
```

### `GetAllChunksAsync` (fallback when FTS returns nothing)

```csharp
public async Task<List<DocumentChunk>> GetAllChunksAsync(string documentId)
{
    await using var conn = new SqliteConnection(_connectionString);
    await conn.OpenAsync();

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = """
        SELECT document_id, page_number, chunk_index, chunk_text
        FROM document_chunks
        WHERE document_id = @docId
        ORDER BY chunk_index
        """;
    cmd.Parameters.AddWithValue("@docId", documentId);

    var results = new List<DocumentChunk>();
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        results.Add(new DocumentChunk(
            reader.GetString(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetString(3)));
    }

    return results;
}
```

### FTS5 query sanitization

```csharp
private static string SanitizeFtsQuery(string query)
{
    // FTS5 treats *, ", AND, OR, NOT, NEAR as operators.
    // Wrap each token in double quotes to treat as literal.
    var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    return string.Join(" OR ", tokens.Select(t => $"\"{t.Replace("\"", "")}\""));
}
```

Each word is quoted and OR'd together so any matching term contributes to ranking. BM25 handles the scoring.

---

## 4. Domain Model

### `DocumentChunk` — add to `AskMyPdf.Core/Models/`

```csharp
namespace AskMyPdf.Core.Models;

public record DocumentChunk(
    string DocumentId,
    int PageNumber,
    int ChunkIndex,
    string ChunkText);
```

---

## 5. Chunking Logic

### `DocumentChunker` — add to `AskMyPdf.Infrastructure/Pdf/`

```csharp
namespace AskMyPdf.Infrastructure.Pdf;

using AskMyPdf.Core.Models;

public class DocumentChunker
{
    private const int TargetChunkSize = 500;   // characters
    private const int OverlapSize = 100;        // characters of overlap between chunks

    public List<DocumentChunk> ChunkDocument(string documentId, List<PageBoundingData> pages)
    {
        var chunks = new List<DocumentChunk>();
        var chunkIndex = 0;

        foreach (var page in pages)
        {
            var pageText = CoordinateTransformer.ReconstructPageText(page);
            if (string.IsNullOrWhiteSpace(pageText))
                continue;

            var pageChunks = SplitIntoChunks(pageText);
            foreach (var chunkText in pageChunks)
            {
                chunks.Add(new DocumentChunk(
                    documentId, page.PageNumber, chunkIndex++, chunkText));
            }
        }

        return chunks;
    }

    private static List<string> SplitIntoChunks(string text)
    {
        if (text.Length <= TargetChunkSize)
            return [text];

        var chunks = new List<string>();
        var sentences = SplitSentences(text);
        var current = new System.Text.StringBuilder();
        var sentenceStartIndices = new List<int>(); // track which sentence started each chunk

        for (var i = 0; i < sentences.Count; i++)
        {
            var sentence = sentences[i];

            if (current.Length > 0 && current.Length + sentence.Length > TargetChunkSize)
            {
                // Emit current chunk
                chunks.Add(current.ToString().Trim());

                // Start new chunk with overlap: walk backwards to include ~OverlapSize chars
                current.Clear();
                var overlapChars = 0;
                for (var j = i - 1; j >= 0 && overlapChars < OverlapSize; j--)
                {
                    current.Insert(0, sentences[j]);
                    overlapChars += sentences[j].Length;
                }
            }

            current.Append(sentence);
        }

        if (current.Length > 0)
            chunks.Add(current.ToString().Trim());

        return chunks;
    }

    private static List<string> SplitSentences(string text)
    {
        // Split on sentence-ending punctuation followed by whitespace or end of string.
        // Keep the delimiter attached to the sentence.
        var sentences = new List<string>();
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is '.' or '!' or '?' or '\n')
            {
                // Check if next char is whitespace or end of string
                if (i + 1 >= text.Length || char.IsWhiteSpace(text[i + 1]))
                {
                    sentences.Add(text[start..(i + 1)]);
                    // Skip whitespace after delimiter
                    var next = i + 1;
                    while (next < text.Length && text[next] is ' ' or '\t')
                        next++;
                    start = next;
                    i = next - 1;
                }
            }
        }

        if (start < text.Length)
            sentences.Add(text[start..]);

        return sentences;
    }
}
```

**Design choices:**
- **500 char target, 100 char overlap** — small enough for precise retrieval, large enough for context. Overlap prevents boundary losses.
- **Sentence-aware splitting** — never cuts mid-sentence. Falls back to full text if the page is short.
- **Per-page chunking** — each chunk knows its source page number, which feeds directly into citations.
- **Uses existing `CoordinateTransformer.ReconstructPageText`** — no new PDF parsing needed.

### Cross-page chunks

Chunks stay within page boundaries. This is intentional:
- Each chunk carries exactly one `pageNumber`, which maps directly to the citation and highlight pipeline.
- Cross-page chunks would need multi-page citation support, adding complexity for marginal gain.
- Overlap between the last chunk of page N and first chunk of page N+1 could be added if cross-page retrieval becomes important, but skip this initially.

---

## 6. Ingestion Extension

### Extend `DocumentService.UploadAsync`

```csharp
public class DocumentService(BoundingBoxExtractor extractor, DocumentChunker chunker, SqliteDb db)
{
    private const int MaxPageCount = 100;

    public async Task<Document> UploadAsync(byte[] pdfBytes, string fileName, long fileSize)
    {
        var pages = extractor.ExtractWordBounds(pdfBytes);

        if (pages.Count > MaxPageCount)
            throw new InvalidOperationException(
                $"PDF exceeds the {MaxPageCount}-page limit. Please upload a shorter document.");

        var doc = new Document(
            Id: Guid.NewGuid().ToString("N"),
            FileName: fileName,
            UploadedAt: DateTime.UtcNow,
            PageCount: pages.Count,
            FileSize: fileSize);

        await db.SaveDocumentAsync(doc, pdfBytes, pages);

        // Index chunks for RAG engine
        var chunks = chunker.ChunkDocument(doc.Id, pages);
        if (chunks.Count > 0)
            await db.SaveChunksAsync(doc.Id, chunks);

        return doc;
    }
}
```

Chunking happens after the existing save so a failed chunk step doesn't block upload. The document is usable by the other two engines regardless.

### DI registration

```csharp
// Program.cs
builder.Services.AddSingleton<DocumentChunker>();
```

---

## 7. RAG Engine Implementation

### `RagAnswerEngine` — add to `AskMyPdf.Infrastructure/Ai/`

```csharp
namespace AskMyPdf.Infrastructure.Ai;

using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anthropic;
using AskMyPdf.Core.Models;
using AskMyPdf.Core.Services;
using AskMyPdf.Infrastructure.Data;
using Microsoft.Extensions.Logging;

public class RagAnswerEngine(
    SqliteDb db,
    AnthropicClient client,
    RagEngineOptions options,
    ILogger<RagAnswerEngine> logger) : IAnswerEngine
{
    public string DisplayName => "RAG (Retrieval)";
    public string Key => "rag";
    public bool NeedsFocusing => false;

    private const int DefaultTopK = 10;

    public async IAsyncEnumerable<AnswerStreamEvent> StreamRawAnswerAsync(
        string question, byte[] pdfBytes, string fileName, string documentId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // 1. Retrieve relevant chunks
        var chunks = await RetrieveChunksAsync(documentId, question);

        if (chunks.Count == 0)
        {
            yield return new AnswerStreamEvent.TextDelta(
                "No indexed content found for this document. The RAG engine requires document chunks to be indexed during upload.");
            yield return new AnswerStreamEvent.Done();
            yield break;
        }

        logger.LogInformation(
            "RAG: retrieved {ChunkCount} chunks for document {DocId}, pages: {Pages}",
            chunks.Count, documentId,
            string.Join(", ", chunks.Select(c => c.PageNumber).Distinct().Order()));

        // 2. Build context block from retrieved chunks
        var context = BuildContext(chunks);

        // 3. Stream answer from Claude with grounded prompt
        var parameters = new MessageCreateParams
        {
            Model = options.Model,
            MaxTokens = 4096,
            System = [new TextBlock { Text = BuildSystemPrompt() }],
            Messages = [
                new MessageParam
                {
                    Role = "user",
                    Content = [
                        new TextBlockParam($"DOCUMENT EXCERPTS:\n\n{context}"),
                        new TextBlockParam($"QUESTION: {question}")
                    ]
                }
            ],
            Stream = true
        };

        var fullAnswer = new StringBuilder();

        await foreach (var streamEvent in client.Messages.CreateStreamingAsync(parameters, ct))
        {
            if (streamEvent is Anthropic.Types.ContentBlockDeltaEvent deltaEvt
                && deltaEvt.Delta.Type == "text_delta")
            {
                var text = deltaEvt.Delta.Text;
                fullAnswer.Append(text);
                yield return new AnswerStreamEvent.TextDelta(text);
            }
        }

        // 4. Parse citations from the answer
        var citations = ParseCitations(fullAnswer.ToString(), chunks, fileName, documentId);
        foreach (var citation in citations)
        {
            yield return new AnswerStreamEvent.CitationReceived(citation);
        }

        yield return new AnswerStreamEvent.Done();
    }

    private async Task<List<DocumentChunk>> RetrieveChunksAsync(string documentId, string question)
    {
        // Try FTS5 search first
        var chunks = await db.SearchChunksAsync(documentId, question, DefaultTopK);

        if (chunks.Count == 0)
        {
            // Fallback: return first N chunks (better than nothing for short documents)
            logger.LogInformation("RAG: FTS5 returned no results, falling back to first {TopK} chunks", DefaultTopK);
            var allChunks = await db.GetAllChunksAsync(documentId);
            chunks = allChunks.Take(DefaultTopK).ToList();
        }

        return chunks;
    }

    private static string BuildContext(List<DocumentChunk> chunks)
    {
        var sb = new StringBuilder();
        foreach (var chunk in chunks.OrderBy(c => c.ChunkIndex))
        {
            sb.AppendLine($"--- [Page {chunk.PageNumber}, Chunk {chunk.ChunkIndex}] ---");
            sb.AppendLine(chunk.ChunkText);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string BuildSystemPrompt() => """
        You are a document Q&A assistant. Answer the user's question based ONLY on the provided document excerpts.

        Rules:
        1. Only use information explicitly stated in the excerpts
        2. If the excerpts do not contain the answer, say: "I could not find an answer to this question in the provided document."
        3. For each claim in your answer, add a citation in this exact format: [Page N] where N is the page number from the excerpt header
        4. After your answer, add a CITATIONS section in this exact format:
           ---CITATIONS---
           [Page N] "exact text copied character-for-character from the excerpt"
           [Page M] "exact text copied character-for-character from the excerpt"
        5. Copy citation text character-for-character from the excerpts — do NOT paraphrase
        6. Each citation snippet should be one focused sentence or short passage
        7. Limit to the 5 most relevant citations
        8. Be concise and direct
        """;

    /// <summary>
    /// Parses the ---CITATIONS--- block from the answer.
    /// Expected format: [Page N] "exact cited text"
    /// </summary>
    private static List<Citation> ParseCitations(
        string answer, List<DocumentChunk> chunks, string fileName, string documentId)
    {
        var citations = new List<Citation>();
        var citationMarker = "---CITATIONS---";
        var markerIndex = answer.IndexOf(citationMarker, StringComparison.OrdinalIgnoreCase);

        if (markerIndex < 0)
            return citations;

        var citationBlock = answer[(markerIndex + citationMarker.Length)..];
        var lines = citationBlock.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("[Page "))
                continue;

            var closeBracket = trimmed.IndexOf(']');
            if (closeBracket < 0)
                continue;

            var pageStr = trimmed[6..closeBracket].Trim();
            if (!int.TryParse(pageStr, out var pageNumber))
                continue;

            // Extract quoted text after the bracket
            var afterBracket = trimmed[(closeBracket + 1)..].Trim();
            var citedText = StripQuotes(afterBracket);

            if (string.IsNullOrWhiteSpace(citedText))
                continue;

            citations.Add(new Citation(
                DocumentId: documentId,
                DocumentName: fileName,
                PageNumber: pageNumber,
                CitedText: citedText,
                HighlightAreas: []));
        }

        return citations;
    }

    private static string StripQuotes(string text)
    {
        var t = text.Trim();
        if (t.Length >= 2 && t[0] == '"' && t[^1] == '"')
            return t[1..^1];
        if (t.Length >= 2 && t[0] == '\u201c' && t[^1] == '\u201d')
            return t[1..^1];
        return t;
    }
}

public record RagEngineOptions(string Model = "claude-sonnet-4-20250514");
```

### Key design decisions in this engine

**Streaming:** The engine streams text deltas in real-time from Claude's API (same as Anthropic engine), so the user sees the answer building progressively.

**Citation format:** The prompt instructs Claude to append a `---CITATIONS---` block at the end with `[Page N] "exact text"` entries. This is parsed after streaming completes. The cited text is then resolved to highlight areas by the existing `QuestionService` pipeline (contiguous matching, since `NeedsFocusing = false`).

**Stripping the citation block from displayed text:** The `---CITATIONS---` block will be streamed to the frontend as part of the text. QuestionService should strip it from the final displayed answer. Add this to `QuestionService.StreamAnswerAsync` just before Phase 2:

```csharp
// Strip citation block from displayed text (RAG engine appends it for parsing)
var citationMarker = "---CITATIONS---";
var markerIdx = completeAnswer.IndexOf(citationMarker, StringComparison.OrdinalIgnoreCase);
if (markerIdx >= 0)
{
    var displayAnswer = completeAnswer[..markerIdx].TrimEnd();
    // Emit a special text event that replaces the full answer with the trimmed version
    // Or: handle this in the frontend by stripping everything after ---CITATIONS---
}
```

**Better approach:** Handle this in the frontend. The SSE `text-delta` events have already been sent. The frontend `useDocumentChat` hook can strip `---CITATIONS---` and everything after it from the accumulated message content. This avoids complicating the backend.

```typescript
// In useDocumentChat.ts, when processing text-delta events:
const citationMarker = '---CITATIONS---';
const markerIdx = content.indexOf(citationMarker);
const displayContent = markerIdx >= 0 ? content.substring(0, markerIdx).trimEnd() : content;
```

---

## 8. DI Registration

### `Program.cs`

```csharp
// AI — RAG engine (Engine C)
var ragEnabled = builder.Configuration.GetValue<bool>("Rag:Enabled");
if (ragEnabled)
{
    builder.Services.AddSingleton(new RagEngineOptions(
        Model: builder.Configuration["Rag:Model"] ?? "claude-sonnet-4-20250514"));
    builder.Services.AddSingleton<DocumentChunker>();
    builder.Services.AddSingleton<IAnswerEngine, RagAnswerEngine>();
}
```

### `appsettings.json`

```json
{
  "Rag": {
    "Enabled": true,
    "Model": "claude-sonnet-4-20250514"
  }
}
```

---

## 9. QuestionService Citation Pipeline — How RAG Fits

The existing Phase 2 in `QuestionService` already handles the RAG engine's output without modification:

```
RAG engine emits:
  TextDelta("The company was founded...")  → forwarded to SSE immediately
  TextDelta("in 2015...")                  → forwarded to SSE immediately
  ...
  CitationReceived(Citation(PageNumber: 3, CitedText: "Founded in 2015 by...", HighlightAreas: []))
  Done

QuestionService Phase 2 (NeedsFocusing = false):
  For each citation:
    → CoordinateTransformer.ToHighlightAreas(citedText, pageNumber, pageBounds, contiguousOnly: true)
    → Yields enriched CitationReceived with HighlightAreas filled in
  Done
```

The exact same code path used by the CLI engine. No changes needed.

---

## 10. File-by-File Change Summary

| File | Change | Lines |
|------|--------|-------|
| `Core/Services/IAnswerEngine.cs` | Add `string documentId` parameter | ~2 |
| `Core/Models/DocumentChunk.cs` | **New file** — record type | ~5 |
| `Infrastructure/Data/SqliteDb.cs` | Add table creation, `SaveChunksAsync`, `SearchChunksAsync`, `GetAllChunksAsync`, update `DeleteDocumentAsync` | ~80 |
| `Infrastructure/Pdf/DocumentChunker.cs` | **New file** — chunking logic | ~80 |
| `Infrastructure/Ai/RagAnswerEngine.cs` | **New file** — engine implementation | ~140 |
| `Infrastructure/Ai/AnthropicAnswerEngine.cs` | Add `documentId` param (ignored) | ~2 |
| `Infrastructure/Ai/ClaudeCliEngine.cs` | Add `documentId` param (ignored) | ~2 |
| `Infrastructure/Services/QuestionService.cs` | Pass `documentId` to engine | ~2 |
| `Infrastructure/Services/DocumentService.cs` | Add chunker DI, call chunker after save | ~5 |
| `Web/Program.cs` | Register RAG engine + chunker | ~8 |
| `client/src/hooks/useDocumentChat.ts` | Strip `---CITATIONS---` from displayed text | ~3 |
| **Total** | | **~330 lines** |

### New files: 3

```
src/AskMyPdf.Core/Models/DocumentChunk.cs
src/AskMyPdf.Infrastructure/Pdf/DocumentChunker.cs
src/AskMyPdf.Infrastructure/Ai/RagAnswerEngine.cs
```

### No changes needed

- `CoordinateTransformer.cs` — works as-is via `contiguousOnly: true` path
- `BoundingBoxExtractor.cs` — unchanged
- `QuestionEndpoints.cs` — unchanged (already passes engine key)
- `DocumentEndpoints.cs` — unchanged
- Frontend engine selector — already dynamic from `/api/engines`, will show RAG automatically

---

## 11. Data Flow Diagrams

### Ingestion (extended)

```
PDF upload
  → BoundingBoxExtractor.ExtractWordBounds(pdfBytes)
  → SqliteDb.SaveDocumentAsync(doc, pdfBytes, pages)         ← existing
  → DocumentChunker.ChunkDocument(docId, pages)              ← NEW
      → CoordinateTransformer.ReconstructPageText(page)      ← reused
      → SplitIntoChunks(pageText)                            ← NEW
  → SqliteDb.SaveChunksAsync(docId, chunks)                  ← NEW
      → INSERT INTO document_chunks                          ← trigger populates FTS5
```

### Query (RAG engine)

```
POST /api/questions { question, documentId, engine: "rag" }
  → QuestionService.StreamAnswerAsync(question, docId, "rag")
      → ResolveEngine("rag") → RagAnswerEngine
      → Load pdfBytes from SQLite (still needed for interface, ignored by engine)

  Phase 1 — RagAnswerEngine.StreamRawAnswerAsync:
      → SqliteDb.SearchChunksAsync(docId, question, topK=10)   ← FTS5 BM25
      → BuildContext(chunks) → "--- [Page 3, Chunk 5] ---\n..."
      → Claude API stream (small context, grounded prompt)
      → yield TextDelta events (real-time streaming)
      → ParseCitations(answer) → yield CitationReceived events
      → yield Done

  Phase 2 — QuestionService shared pipeline (NeedsFocusing=false):
      → Load page_bounds for cited pages
      → For each citation:
          CoordinateTransformer.ToHighlightAreas(citedText, pageNumber, pageBounds, contiguousOnly: true)
      → yield enriched CitationReceived with HighlightAreas
      → yield Done

  → SSE to frontend → chat panel + PDF viewer highlights
```

---

## 12. Testing Plan

### Unit tests

```csharp
// DocumentChunkerTests.cs
[Fact] ShortPage_SingleChunk()          // page < 500 chars → 1 chunk
[Fact] LongPage_MultipleChunks()        // page > 500 chars → multiple with overlap
[Fact] OverlapContainsPreviousSentence() // overlap region includes trailing sentence from prior chunk
[Fact] EmptyPage_NoChunks()             // whitespace-only page skipped
[Fact] PreservesPageNumber()            // each chunk carries correct source page

// RagAnswerEngineTests.cs
[Fact] ParseCitations_ValidBlock()       // parses [Page N] "text" format
[Fact] ParseCitations_NoCitationBlock()  // returns empty list
[Fact] ParseCitations_MixedFormats()     // handles curly quotes, missing quotes
[Fact] StripQuotes_VariousStyles()       // straight, curly, none

// SqliteDb chunk tests
[Fact] SaveAndSearchChunks_BM25()        // round-trip: save, search, verify ranking
[Fact] DeleteDocument_CascadesChunks()   // delete doc → chunks + FTS entries gone
[Fact] SearchChunks_NoResults_Fallback() // FTS returns nothing → engine falls back to first N
```

### Integration test

```csharp
[Fact]
public async Task RagEngine_EndToEnd()
{
    // 1. Upload a PDF
    // 2. Verify chunks were created in SQLite
    // 3. Ask a question with engine=rag
    // 4. Verify SSE stream contains text-delta + citation events
    // 5. Verify citations have highlight areas (contiguous path)
}
```

---

## 13. Configuration Reference

```json
{
  "Rag": {
    "Enabled": true,
    "Model": "claude-sonnet-4-20250514"
  }
}
```

| Key | Default | Description |
|-----|---------|-------------|
| `Rag:Enabled` | `false` | Register RAG engine in DI |
| `Rag:Model` | `claude-sonnet-4-20250514` | Model for grounded answering from chunks |

---

## 14. Future: Phase 2 (Vector RAG)

Not for now. But the upgrade path when ready:

1. Add an `IChunkEmbedder` interface (wraps an embedding API — OpenAI, Cohere, or a local model)
2. Add a `chunk_embeddings` table or store vectors as BLOBs in `document_chunks`
3. Replace FTS5 search with cosine similarity search
4. Optionally add a reranking step (cross-encoder or Claude-based)
5. Everything downstream (engine contract, citation pipeline, highlights) stays identical

The Phase 1 implementation is designed so that swapping the retrieval method is localized to `SqliteDb.SearchChunksAsync` + the new embedder. The engine itself doesn't care how chunks were retrieved.
