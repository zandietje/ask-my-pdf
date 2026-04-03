# Implementation Plan: AskMyPdf — Document Q&A Application

## Context

Building a Document Q&A web application for a Plot Solutions developer assessment. Users upload PDFs, ask questions, and receive grounded answers with source citations. Clicking a citation highlights the exact passage in a PDF viewer. The project must be built from scratch (no prebuilt AI templates) in 3-4 days.

**Deadline**: June 4, 2026. Today: April 3, 2026.

**Current state**: Empty project — only `CLAUDE.md` and `.claude/` config files exist. No code, no git repo.

---

## Architecture Decision: Direct PDF + Citations API (No RAG)

### Why NOT RAG?

After thorough investigation, the original RAG pipeline (PdfPig → chunking → OpenAI embeddings → vector store → cosine similarity → query reformulation → Claude) is **unnecessary and inferior** for this use case:

1. **Answer quality**: Academic benchmarks (LaRA, ICML 2025) show full-context LLM outperforms chunk-based RAG by ~15% on cross-section questions with strong models like Claude. RAG's most common failure mode — missed chunks — is eliminated entirely.

2. **Citation accuracy**: Claude's Citations API returns structured `page_location` citations with `cited_text` + `start_page_number` when given a PDF directly. These are **extracted, not generated** — guaranteed to be valid. No prompt engineering needed.

3. **Complexity**: RAG requires ~8 services (extractor, chunker, embedder, vector store, reformulator, retriever, generator, transformer). Direct PDF requires ~3 (PDF storage, Claude API caller, bounding box matcher).

4. **Dependency reduction**: Eliminates OpenAI API entirely (no embeddings needed). Single API key: Anthropic.

5. **Assessment fit**: The spec says "clean, machine-readable PDFs" and criterion #1 is "Does it work?" — fewer moving parts means fewer failure modes.

### Why NOT Agentic File Search?

Investigated all options:
- **OpenAI File Search** (Assistants/Responses API): Does NOT return page numbers. Fatal for source identification.
- **Gemini File Search**: Does NOT return page numbers.
- **Cohere Command R**: No page numbers, no direct PDF support.
- **Azure AI Search**: Massive infrastructure overkill, requires Azure subscription.

**Claude Citations API is the only LLM API that returns structured page numbers + exact quoted text for PDFs.**

### What We Build Instead

```
Upload PDF → PdfPig extracts word bounding boxes per page → Store in SQLite
Ask question → Send full PDF (base64) to Claude API with citations enabled (prompt cached)
            → Claude streams answer + page_location citations (cited_text + page number)
            → Server matches cited_text to PdfPig bounding boxes on that page
            → Stream text + citations + highlight coordinates to frontend via SSE
            → Frontend highlights exact passage in PDF viewer
```

### Cost Analysis (Claude Sonnet, 30-page PDF, 10 questions)

| Operation | Cost |
|-----------|------|
| First question (cache write) | ~$0.23 |
| Questions 2-10 (cache read, 90% discount) | ~$0.02 each |
| **Total for 10 questions** | **~$0.39** |

Prompt caching makes repeated questions on the same document 90% cheaper. The 5-minute cache TTL refreshes on each hit.

---

## Confirmed Decisions

- **React 18** (not 19) — `@react-pdf-viewer` was archived March 25, 2026; React 18 guarantees compatibility
- **Custom SSE hook** (not Vercel AI SDK) — AI SDK v5 has breaking changes; custom hook gives full control over citation events (~50 lines)
- **Anthropic API key only** — no OpenAI key needed (no embeddings)
- **Direct PDF upload** to Claude API — no chunking, no embeddings, no vector store
- **PdfPig for bounding boxes only** — not for RAG text extraction, just for mapping cited_text to precise coordinates

## Key Technical Details

- Claude Citations API returns `page_location` citations when given a base64 PDF: `{ cited_text, start_page_number, end_page_number, document_index }`
- `cited_text` is extracted from the document, not generated — always matches the source
- `cited_text` may contain whitespace/formatting artifacts — need normalization for bounding box matching
- Citations API is **incompatible with structured outputs** — parse citations from the stream
- PdfPig coordinates are bottom-left origin (Y up); PDF viewer uses top-left (Y down) — need coordinate transformation
- Prompt caching requires `cache_control: { type: "ephemeral" }` on the document block. Minimum 1,024 tokens for Sonnet (any reasonable PDF exceeds this)

---

## Phase 1: Project Scaffold (2-3 hrs)

Create the full project structure so `dotnet build` and `npm run dev` both succeed.

### .NET Solution (3 projects)
- `AskMyPdf.sln` at root
- `src/AskMyPdf.Web/` — .NET 8 Minimal API (entry point, serves API + static frontend)
- `src/AskMyPdf.Core/` — Domain models, DTOs, service interfaces (zero external dependencies)
- `src/AskMyPdf.Infrastructure/` — PdfPig, Claude API, SQLite implementations
- `tests/AskMyPdf.Tests/` — xUnit + FluentAssertions
- `Directory.Build.props` — shared `net8.0`, nullable enable, implicit usings

### NuGet Packages
- **Infrastructure**: `PdfPig` 0.1.14, `Anthropic` 12.11.0, `Microsoft.Data.Sqlite`
- **Tests**: `xunit`, `FluentAssertions`, `Microsoft.NET.Test.Sdk`
- No `OpenAI` package needed

### React Frontend
- `client/` — React 18 + Vite + TypeScript (strict)
- Tailwind CSS + shadcn/ui (via CLI init)
- `@react-pdf-viewer/core` + `@react-pdf-viewer/highlight` + `pdfjs-dist`
- `react-resizable-panels`, `lucide-react`
- `vite.config.ts` — proxy `/api` to `http://localhost:5000`

### Skeleton files
- `src/AskMyPdf.Web/Program.cs` — builder + DI placeholders + placeholder endpoints
- `src/AskMyPdf.Web/appsettings.json` — config structure for `Anthropic:ApiKey`
- `client/src/App.tsx` — "Hello World" shell
- `.gitignore` — .NET + Node standard
- Git init + first commit

### Core Models (created early, used everywhere)
- `src/AskMyPdf.Core/Models/Document.cs` — Id, FileName, UploadedAt, PageCount, FileSize
- `src/AskMyPdf.Core/Models/PageBoundingData.cs` — PageNumber, PageWidth, PageHeight, Words (list of WordBoundingBox)
- `src/AskMyPdf.Core/Models/WordBoundingBox.cs` — Text, Left, Bottom, Right, Top (PDF units)
- `src/AskMyPdf.Core/Models/HighlightArea.cs` — PageIndex (0-based), Left, Top, Width, Height (percentages 0-100)
- `src/AskMyPdf.Core/Models/Citation.cs` — DocumentId, DocumentName, PageNumber, CitedText, HighlightAreas

### Verify
- `dotnet build AskMyPdf.sln` passes
- `cd client && npm install && npm run dev` starts
- `dotnet run --project src/AskMyPdf.Web` starts on port 5000

---

## Phase 2: PDF Upload + Bounding Box Extraction + SQLite (3-4 hrs)

Accept PDF uploads, extract word-level bounding boxes via PdfPig (for highlighting only), store everything in SQLite.

### Files to create
- `src/AskMyPdf.Infrastructure/Pdf/BoundingBoxExtractor.cs`
  - `ExtractWordBounds(Stream pdfStream) -> List<PageBoundingData>`
  - Uses `PdfDocument.Open()`, `page.GetWords()` for bounding boxes
  - Stores `page.Height`/`page.Width` per page for coordinate transformation
  - Does NOT do text extraction for RAG — this is only for highlight coordinate lookup
- `src/AskMyPdf.Infrastructure/Pdf/CoordinateTransformer.cs`
  - `ToHighlightAreas(string citedText, int pageNumber, List<PageBoundingData> pages) -> List<HighlightArea>`
  - Fuzzy-matches `cited_text` against word sequence on the cited page
  - Converts matched word bounding boxes from PdfPig coords (bottom-left, Y up) to viewer percentages (top-left, Y down):
    ```
    left_pct   = (word.Left / pageWidth) * 100
    top_pct    = ((pageHeight - word.Top) / pageHeight) * 100
    width_pct  = ((word.Right - word.Left) / pageWidth) * 100
    height_pct = ((word.Top - word.Bottom) / pageHeight) * 100
    ```
  - Groups consecutive words into line-level highlight rectangles
- `src/AskMyPdf.Infrastructure/Data/SqliteDb.cs`
  - `InitializeAsync()` — creates `documents` + `stored_files` + `page_bounds` tables
  - CRUD: `SaveDocumentAsync`, `GetDocumentAsync`, `GetAllDocumentsAsync`, `GetFileAsync`
  - `SavePageBoundsAsync` / `GetPageBoundsAsync` — store/retrieve bounding box data per document
- `src/AskMyPdf.Core/Services/DocumentService.cs`
  - Orchestrates: receive file → extract bounding boxes → save file + metadata + bounds to SQLite
- `src/AskMyPdf.Web/Endpoints/DocumentEndpoints.cs`
  - `POST /api/documents/upload` — accepts IFormFile, validates PDF, extracts bounds, saves
  - `GET /api/documents` — list all
  - `GET /api/documents/{id}` — single document
  - `GET /api/documents/{id}/file` — returns original PDF binary (for viewer)
- `src/AskMyPdf.Web/Dtos/DocumentDto.cs`, `UploadResponse.cs`

### Tests
- `tests/AskMyPdf.Tests/CoordinateTransformerTests.cs` — known coordinate values, edge cases
- `tests/AskMyPdf.Tests/BoundingBoxExtractorTests.cs` — word extraction from test PDF

### Verify
- `curl -F "file=@test.pdf" http://localhost:5000/api/documents/upload` returns id + metadata
- `GET /api/documents` returns uploaded doc
- `GET /api/documents/{id}/file` returns PDF binary
- SQLite file created with data + bounding boxes
- `dotnet test` passes

---

## Phase 3: Question Pipeline + Claude Citations + SSE Streaming (4-5 hrs)

The most critical phase — sends the full PDF to Claude with citations enabled and streams the answer back.

### Files to create
- `src/AskMyPdf.Infrastructure/Ai/ClaudeService.cs` — **Most critical file**
  - Constructs Claude API request with base64 PDF document block:
    ```csharp
    var pdfBase64 = Convert.ToBase64String(pdfBytes);

    var parameters = new MessageCreateParams
    {
        Model = "claude-sonnet-4-5-20250514",
        MaxTokens = 4096,
        System = new SystemPrompt(groundingPrompt),
        Messages = [new()
        {
            Role = Role.User,
            Content = [
                new DocumentBlockParam(new Base64PdfSource(pdfBase64))
                {
                    Title = document.FileName,
                    Citations = new CitationsConfigParam { Enabled = true },
                    CacheControl = new CacheControlEphemeral(),
                },
                new TextBlockParam { Text = question },
            ],
        }],
    };
    ```
  - Uses `client.Messages.CreateStreaming(parameters)` for streaming
  - Parses `text_delta` and `citations_delta` events from the stream
  - For each `page_location` citation: extracts `cited_text` + `start_page_number`
  - `StreamAnswerAsync(question, pdfBytes, fileName) -> IAsyncEnumerable<AnswerStreamEvent>`
- `src/AskMyPdf.Core/Services/QuestionService.cs`
  - Orchestrates: load PDF bytes from SQLite → call ClaudeService → for each citation, resolve bounding boxes via CoordinateTransformer → yield enriched events
  - If Claude responds with no citations and "cannot find" → pass through gracefully
- `src/AskMyPdf.Core/Models/AnswerStreamEvent.cs`
  - `TextDelta(string Text)` — streaming text chunk
  - `CitationReceived(Citation)` — citation with page number + highlight areas
  - `Done()` — stream complete
- `src/AskMyPdf.Web/Endpoints/QuestionEndpoints.cs`
  - `POST /api/questions` — SSE streaming endpoint
  - Request body: `{ question: string, documentId: string }`
  - Sets `Content-Type: text/event-stream`, `Cache-Control: no-cache`
  - Writes events: `event: text-delta\ndata: {...}\n\n`, `event: citation\ndata: {...}\n\n`, `event: done\ndata: {}\n\n`

### Grounding Prompt (System)
```
You are a document Q&A assistant. Answer the user's question based ONLY on the provided document.

Rules:
1. Only use information explicitly stated in the document
2. If the document does not contain the answer, say: "I could not find an answer to this question in the provided document."
3. Quote relevant text to support your answer
4. If multiple sections are relevant, reference all of them
5. Never make up or infer information beyond what is explicitly stated
6. Be concise and direct
```

### Key integration: Citation → Bounding Box mapping
```
Claude returns: { cited_text: "Founded in 2015", start_page_number: 3 }
Server does:
  1. Load PageBoundingData for page 3 from SQLite
  2. Normalize cited_text (collapse whitespace, strip control chars like \u0002)
  3. Sliding window match against word sequence on page 3
  4. Get bounding boxes for matched words
  5. Transform to percentage-based HighlightAreas
  6. Return citation with highlight coordinates in SSE event
```

### Verify
- `curl -X POST /api/questions -d '{"question":"...", "documentId":"..."}' -N` → SSE stream with text-delta, citation, done events
- Citations include valid page numbers and highlight areas
- "I don't know" test: question unrelated to document content → appropriate response
- End-to-end with real PDF: upload → ask → answer with citations
- Second question on same document is faster (prompt cache hit)

### Requires: **Anthropic API key** via user secrets

### Risk: Anthropic C# SDK beta types
If the typed API doesn't expose citations cleanly, fallback to raw `HttpClient` + manual JSON for the citation-specific request. Budget 1-2 hrs for SDK exploration.

### Risk: cited_text matching
Claude's `cited_text` may have whitespace differences vs PdfPig's extracted words. Mitigation: normalize both sides (collapse whitespace, lowercase), use sliding window substring match with tolerance. Fall back to page-level highlighting (just navigate to page) if text match fails.

---

## Phase 4: React UI — Chat + Upload (3-4 hrs)

Build the chat panel, upload UI, and custom SSE streaming hook.

### Files to create
- `client/src/hooks/useDocumentChat.ts` — **Frontend nerve center**
  - Custom hook using `fetch()` + `ReadableStream` to consume SSE from `POST /api/questions`
  - Parses SSE format: buffer accumulates text, splits on `\n\n`, extracts `event:` and `data:` lines
  - On `text-delta`: appends to current message text
  - On `citation`: adds to current message citations array
  - On `done`: finalizes message
  - Exposes: `messages`, `isLoading`, `sendMessage(question, documentId)`
- `client/src/lib/api.ts` — `uploadDocument`, `getDocuments`, `getDocumentFileUrl`
- `client/src/lib/types.ts` — TypeScript types mirroring backend DTOs
- `client/src/components/layout/AppLayout.tsx` — `react-resizable-panels` split (40% chat / 60% PDF)
- `client/src/components/chat/ChatPanel.tsx` — message list + input, auto-scroll
- `client/src/components/chat/MessageBubble.tsx` — user/assistant messages, streaming cursor
- `client/src/components/chat/CitationChip.tsx` — clickable `[doc.pdf, Page 3]` badges with cited text preview
- `client/src/components/upload/UploadDropzone.tsx` — drag-and-drop PDF upload with progress
- `client/src/components/upload/DocumentList.tsx` — list of uploaded docs, click to select

### shadcn/ui components to install
Button, Input, Card, ScrollArea, Badge, Separator, Skeleton

### Verify
- App renders split-pane layout
- Can upload PDF, see in document list
- Can type question, see streaming answer appear
- Citation chips render below/inline with assistant messages

---

## Phase 5: PDF Viewer + Citation Highlighting (3-4 hrs)

The visual differentiator — click a citation, see it highlighted in the PDF.

### Files to create
- `client/src/components/pdf/PdfViewerPanel.tsx`
  - `@react-pdf-viewer/core` + highlight plugin
  - Loads PDF from `/api/documents/{id}/file`
  - Accepts `highlightAreas` prop from active citation
  - `jumpToHighlightArea(area)` on citation click
  - Yellow overlay rectangles via `renderHighlights` callback
- `client/src/components/pdf/HighlightLayer.tsx` — renders highlight overlays
- `client/src/lib/pdfWorker.ts` — configure pdfjs-dist worker URL
- Update `App.tsx` — wire citation click → set active citation → PDF viewer highlights + scrolls

### Interaction flow
1. User clicks `[Page 7]` chip in chat
2. App sets `activeCitation` state (contains highlight areas + document ID)
3. If different document, PDF viewer loads new document
4. `jumpToHighlightArea(firstArea)` scrolls to correct page
5. All highlight areas render as yellow overlays on the cited text

### Verify
- Click citation → PDF scrolls to correct page
- Yellow highlight appears over cited text (word-level precision)
- Switching between citations from different pages works
- Visual alignment of highlights matches actual text position

### Fallback: Text-search highlighting
If bounding box matching proves unreliable for certain PDFs, fall back to:
1. Navigate to the cited page
2. Use `@react-pdf-viewer/search` plugin to `highlight([citedText])` with `setTargetPages` limited to that page
3. Visually equivalent, slightly less precise for edge cases

### Risk: @react-pdf-viewer compatibility
If the archived package fails with React 18, pivot to `react-pdf` (wojtekmaj, actively maintained) with `customTextRenderer` for highlighting. Different API but achieves the same result.

---

## Phase 6: Polish + Documentation (3-4 hrs)

Assessment readiness — error handling, edge cases, and the graded README.

### Work items
- Input validation: PDF-only, size limits (32MB max for Claude API), non-empty questions
- Error handling: API key missing, Claude API timeout, PDF too large (>100 pages)
- Loading states: skeleton UI during PDF upload/processing, typing indicator during streaming
- Empty states: "Upload a PDF to get started"
- Edge cases: empty PDF, very short PDF, irrelevant questions → "I could not find an answer"
- Static file serving for production: `UseStaticFiles` + SPA fallback to `index.html`

### README.md (graded deliverable)
- Project overview + screenshot
- Architecture diagram (the simplified one)
- Prerequisites: Anthropic API key, .NET 8 SDK, Node.js 18+
- Setup + run instructions (step by step, clone-and-run ready)
- Design decisions:
  - Why direct PDF + Citations API instead of RAG (with evidence)
  - Why PdfPig for bounding boxes
  - Why prompt caching for cost efficiency
  - Coordinate transformation explanation
  - Highlighting approach
- Known limitations (100-page limit, API cost per query) + future improvements

### Final verification
- Full demo flow: upload → ask → answer with citations → click → highlight
- `dotnet build` passes, `dotnet test` passes, `npm run build` passes
- README is clone-and-run ready
- Record demo video or deploy

---

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                 React 18 + Vite + TypeScript                    │
│  ┌─────────────────────┐    ┌──────────────────────────────┐   │
│  │    Chat Panel        │    │    PDF Viewer Panel           │   │
│  │  Streaming answer    │    │  @react-pdf-viewer/highlight  │   │
│  │  [Page 7] clickable ─┼───>│  jumpToHighlightArea(coords)  │   │
│  └──────────┬───────────┘    └──────────────────────────────┘   │
│             │ SSE stream                                         │
└─────────────┼───────────────────────────────────────────────────┘
              │
┌─────────────▼───────────────────────────────────────────────────┐
│              .NET 8 Minimal API                                  │
│  POST /api/documents/upload    POST /api/questions (SSE)         │
│  GET  /api/documents           GET  /api/documents/{id}/file     │
└─────────────┬───────────────────────────────────────────────────┘
              │
┌─────────────▼───────────────────────────────────────────────────┐
│              Core Services                                       │
│  ┌──────────────────┐  ┌──────────────────────────────────────┐ │
│  │ DocumentService   │  │ QuestionService                      │ │
│  │ - Store PDF file  │  │ 1. Load PDF bytes from SQLite        │ │
│  │ - PdfPig: extract │  │ 2. Send base64 PDF to Claude API     │ │
│  │   word bounding   │  │    (citations enabled, cached)       │ │
│  │   boxes per page  │  │ 3. Stream text + citations           │ │
│  │ - Store metadata  │  │ 4. Match cited_text → bounding boxes │ │
│  │   in SQLite       │  │    via CoordinateTransformer         │ │
│  └──────────────────┘  │ 5. Return via SSE                    │ │
│                         └──────────────────────────────────────┘ │
└─────────────┬───────────────────────────────────────────────────┘
              │
┌─────────────▼───────────────────────────────────────────────────┐
│              Infrastructure                                      │
│  ┌────────┐  ┌──────────────┐  ┌──────────┐                    │
│  │ PdfPig │  │ Claude API   │  │ SQLite   │                    │
│  │ BBox   │  │ + Citations  │  │ Doc Meta │                    │
│  │ Extract│  │ + Caching    │  │ + Files  │                    │
│  └────────┘  └──────────────┘  │ + BBoxes │                    │
│                                 └──────────┘                    │
│  No OpenAI    No Vector Store    No Embeddings    No Chunker    │
└──────────────────────────────────────────────────────────────────┘
```

## Project Structure

```
ask-my-pdf/
├── src/
│   ├── AskMyPdf.Web/              # .NET 8 Minimal API
│   │   ├── Program.cs           # Entry point, DI, middleware
│   │   ├── Endpoints/           # DocumentEndpoints, QuestionEndpoints
│   │   ├── Dtos/                # API request/response DTOs
│   │   └── appsettings.json     # Anthropic:ApiKey config
│   ├── AskMyPdf.Core/             # Domain models, service interfaces
│   │   ├── Models/              # Document, Citation, HighlightArea, etc.
│   │   └── Services/            # DocumentService, QuestionService
│   └── AskMyPdf.Infrastructure/   # External integrations
│       ├── Ai/                  # ClaudeService (API + citations + streaming)
│       ├── Pdf/                 # BoundingBoxExtractor, CoordinateTransformer
│       └── Data/                # SqliteDb
├── client/                       # React 18 + Vite + TypeScript
│   ├── src/
│   │   ├── components/
│   │   │   ├── chat/            # ChatPanel, MessageBubble, CitationChip
│   │   │   ├── pdf/             # PdfViewerPanel, HighlightLayer
│   │   │   ├── upload/          # UploadDropzone, DocumentList
│   │   │   └── layout/          # AppLayout (split pane)
│   │   ├── hooks/               # useDocumentChat (custom SSE hook)
│   │   ├── lib/                 # api.ts, types.ts, pdfWorker.ts
│   │   ├── App.tsx
│   │   └── main.tsx
│   ├── vite.config.ts           # proxy /api to .NET backend
│   └── package.json
├── tests/
│   └── AskMyPdf.Tests/            # xUnit + FluentAssertions
├── AskMyPdf.sln
├── Directory.Build.props
├── CLAUDE.md
├── PLAN.md
└── README.md
```

## Tech Stack

| Layer | Choice | Package | Rationale |
|-------|--------|---------|-----------|
| Backend | .NET 8 Minimal API | — | Required by assessment |
| Frontend | React 18 + Vite + TS | `react`, `vite`, `typescript` | Best PDF viewer ecosystem |
| UI Components | shadcn/ui + Tailwind | `tailwindcss`, shadcn CLI | Modern, clean, no lock-in |
| PDF Viewer | @react-pdf-viewer | `@react-pdf-viewer/core`, `/highlight` | Programmatic highlighting, `jumpToHighlightArea` |
| Split Layout | react-resizable-panels | `react-resizable-panels` | Chat left, PDF right |
| LLM + Citations | Claude API (direct PDF) | `Anthropic` (12.11.0) | Only API with structured page citations for PDFs |
| PDF Bounding Boxes | PdfPig | `PdfPig` (0.1.14) | Word-level coordinates for precise highlighting |
| Database | SQLite | `Microsoft.Data.Sqlite` | Single file, zero setup |
| Testing | xUnit + FluentAssertions | — | .NET standard |

### What's NOT in the stack (and why)
- **No OpenAI** — no embeddings needed (no RAG)
- **No vector store** — no embeddings to store
- **No chunking** — Claude sees the full PDF
- **No query reformulation** — Claude has full context, doesn't need query optimization
- **No Vercel AI SDK** — v5 breaking changes; custom SSE hook is ~50 lines

## Time Budget

| Phase | Description | Hours | Day |
|-------|-------------|-------|-----|
| 1 | Scaffold | 2-3 | Day 1 |
| 2 | Upload + BBox Extraction + SQLite | 3-4 | Day 1 |
| 3 | Question Pipeline + Citations + SSE | 4-5 | Day 1-2 |
| 4 | React Chat + Upload UI | 3-4 | Day 2 |
| 5 | PDF Viewer + Highlighting | 3-4 | Day 2-3 |
| 6 | Polish + Documentation | 3-4 | Day 3 |
| **Total** | | **19-24 hrs** | **3 days** |

Time saved vs original plan: ~4-6 hours (eliminated chunking, embeddings, vector store, query reformulation).

---

## Top Risks

1. **Anthropic C# SDK beta** — citation types or PDF document blocks may differ from docs. Mitigation: explore SDK early in Phase 3, fallback to raw `HttpClient` + manual JSON.
2. **cited_text → bounding box matching** — whitespace/formatting differences between Claude's `cited_text` and PdfPig's extracted words. Mitigation: normalize both sides, sliding window match, fall back to page-level navigation.
3. **@react-pdf-viewer archived** — may have bugs with React 18. Mitigation: try first, fallback to `react-pdf` (wojtekmaj) with `customTextRenderer`.
4. **PDF size limits** — Claude API max 100 pages (200k context models), 32MB. Mitigation: validate on upload, show clear error for oversized PDFs. Assessment says "clean, machine-readable" — unlikely to hit this.
5. **Bounding box alignment** — PdfPig coords may not align with viewer for certain PDFs ("Print as PDF" from Word has known issues). Mitigation: test with multiple PDFs, fall back to text-search highlighting.

## Prerequisites Before Starting

- [ ] Anthropic API key (for Claude LLM + Citations + PDF processing)
- [ ] .NET 8 SDK installed
- [ ] Node.js 18+ installed
- [ ] A test PDF file for end-to-end testing
