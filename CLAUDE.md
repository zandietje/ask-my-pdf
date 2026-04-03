# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Purpose

**Document Q&A Application** — a web app for uploading PDFs and asking natural language questions about their content. Every answer must be grounded in document text and cite the exact source (document name, page number, relevant snippet). Clicking a citation should show the highlighted passage in the PDF.

This is a developer assessment for Plot Solutions. Target: complete in 3–4 focused days.

### Evaluation Criteria (in the assessor's exact order)

1. **"Does it work?"** — ship a functional app above all else
2. **"Code quality and organization"** — clean architecture, separation of concerns
3. **"User experience"** — polished, usable UI with real-time streaming
4. **"Accuracy of source identification"** — THE technical differentiator. Every answer must point back to where in the document it came from, with visual highlighting in the PDF viewer
5. **"Documentation"** — README with setup instructions + design decisions explanation

### Deliverables

- Source code (GitHub repository)
- README with setup/run instructions + design decisions
- Working demo (deployed URL or video walkthrough)

## Hard Rules

1. **Backend must be .NET** — the only hard technology constraint
2. **No prebuilt AI templates, boilerplates, or starter kits** — build the pipeline yourself. Every piece must be understood and explainable
3. **No overengineering** — KISS and YAGNI. This is a 3–4 day project
4. **Working demo > perfect architecture** — criterion #1 is "Does it work?"
5. **Every answer must cite its source** — document name + page number + snippet + visual PDF highlight
6. **No unnecessary abstractions** — no interfaces/factories/patterns unless solving a real problem today
7. **Fail fast** — validate inputs early, return clear errors

## Tech Stack (Locked In)

| Layer | Choice | Package | Rationale |
|-------|--------|---------|-----------|
| Backend | .NET 8 Minimal API | — | Required. Serves API + static frontend files |
| Frontend | **React 18 + Vite + TypeScript** | `react`, `vite`, `typescript` | Best PDF viewer ecosystem |
| UI Components | **shadcn/ui + Tailwind CSS** | `tailwindcss`, shadcn CLI | Modern, clean, no dependency lock-in |
| AI Chat UI | **Custom SSE hook** | — | ~50 lines, full control over citation events (Vercel AI SDK v5 has breaking changes) |
| PDF Viewer | **@react-pdf-viewer** | `@react-pdf-viewer/core`, `@react-pdf-viewer/highlight` | Programmatic highlighting, `jumpToHighlightArea`, page navigation |
| Split Layout | **react-resizable-panels** | `react-resizable-panels` | Chat left, PDF viewer right, draggable divider |
| PDF Bounding Boxes | **PdfPig** | `PdfPig` (0.1.14) | Word-level bounding box extraction for precise highlighting |
| LLM + Citations | **Claude API with direct PDF + Citations** | `Anthropic` (12.11.0) | Send PDF directly, get structured page_location citations |
| Database | **SQLite** | `Microsoft.Data.Sqlite` | Single file, zero setup, document metadata + bounding boxes |
| Testing | xUnit + FluentAssertions | — | .NET standard |

### Why Direct PDF + Citations API instead of RAG?

Academic benchmarks (LaRA, ICML 2025) show full-context LLM outperforms chunk-based RAG by ~15% on cross-section questions. RAG's most common failure mode (missed chunks) is eliminated. Claude's Citations API returns structured `page_location` citations with exact `cited_text` + `start_page_number` — extracted, not generated, guaranteed valid. Eliminates need for embeddings, vector store, chunking, and query reformulation. Single API dependency (Anthropic only, no OpenAI).

### Why NOT RAG / Agentic File Search / Other Options?

- **Custom RAG pipeline**: Inferior answer quality (only sees top-K chunks), more failure modes, requires OpenAI API for embeddings, 60% more code to write
- **OpenAI File Search**: Does NOT return page numbers — fatal for criterion #4
- **Gemini File Search**: Does NOT return page numbers
- **Azure AI Search**: Massive infrastructure overkill
- **Semantic Kernel / Kernel Memory**: Assessment says "no prebuilt AI templates"

## Architecture

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
│  │   word bounding   │  │    (citations enabled, prompt cached)│ │
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
│   ├── AskMyPdf.Web/              # .NET 8 Minimal API (serves API + static frontend)
│   │   ├── Endpoints/           # DocumentEndpoints, QuestionEndpoints
│   │   └── Dtos/                # API request/response DTOs
│   ├── AskMyPdf.Core/             # Domain models, service interfaces
│   │   ├── Models/              # Document, Citation, HighlightArea, etc.
│   │   └── Services/            # DocumentService, QuestionService
│   └── AskMyPdf.Infrastructure/   # PdfPig, Claude API, SQLite
│       ├── Ai/                  # ClaudeService (direct PDF + citations + streaming)
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
│   └── AskMyPdf.Tests/            # xUnit tests
├── AskMyPdf.sln
├── CLAUDE.md
└── README.md
```

## Pipeline Design: Direct PDF + Citations API

### Why This Approach?

Instead of building a traditional RAG pipeline, we send the full PDF directly to Claude's API with citations enabled. This is superior because:
1. **Better answers** — Claude sees the entire document, not just retrieved chunks
2. **Guaranteed citations** — `cited_text` is extracted from the document, not hallucinated
3. **Simpler code** — no chunking, embeddings, vector store, or query reformulation
4. **Single API dependency** — Anthropic only (no OpenAI needed)

### Ingest Flow (on PDF upload)
```
PDF file
→ Store original file in SQLite (for serving to viewer + sending to Claude)
→ PdfPig: extract word bounding boxes per page (for highlighting only)
→ Store bounding box data in SQLite
→ Store document metadata (name, page count, size, upload time)
```

### Query Flow (on question, streamed via SSE)
```
User question + document ID
→ Load PDF bytes from SQLite
→ Send base64 PDF to Claude API as DocumentBlockParam
  - citations: { enabled: true }
  - cache_control: { type: "ephemeral" } (90% cost savings on repeat questions)
→ Claude streams answer with page_location citations:
  { cited_text: "...", start_page_number: N }
→ For each citation:
  - Load word bounding boxes for page N from SQLite
  - Fuzzy-match cited_text against word sequence on that page
  - Convert matched PdfPig coords → percentage-based HighlightAreas
→ Stream text-delta + enriched citation events via SSE to frontend
→ Frontend renders answer with clickable [Page N] chips
→ Click citation → PDF viewer jumps to page, highlights passage with yellow overlay
```

### Claude Citations API Response (page_location type)

When sending a PDF with `citations: { enabled: true }`, Claude returns:
```json
{
  "type": "page_location",
  "cited_text": "The exact text being cited",
  "document_index": 0,
  "document_title": "Document Title",
  "start_page_number": 3,
  "end_page_number": 4
}
```
- `start_page_number` is 1-indexed
- `end_page_number` is exclusive (start:3, end:4 means page 3 only)
- `cited_text` is extracted from the document, not generated — always real

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

### Source Citation Display
The frontend shows citations as clickable chips inline/below the answer:
```
[Page 7] "Founded in 2015 by Jane Smith..."
```
Clicking jumps the PDF viewer to page 7 and highlights the passage using bounding box coordinates.

## Frontend-Backend Integration

### SSE Streaming

The .NET API streams answers via SSE. The frontend uses a custom hook (~50 lines) to consume the stream.

```csharp
// .NET endpoint streams SSE events
app.MapPost("/api/questions", async (QuestionRequest req, QuestionService svc, HttpResponse res) =>
{
    res.ContentType = "text/event-stream";
    res.Headers.CacheControl = "no-cache";

    await foreach (var evt in svc.StreamAnswerAsync(req.Question, req.DocumentId))
    {
        var eventType = evt switch {
            TextDelta => "text-delta",
            CitationReceived => "citation",
            Done => "done",
        };
        await res.WriteAsync($"event: {eventType}\ndata: {JsonSerializer.Serialize(evt)}\n\n");
        await res.Body.FlushAsync();
    }
});
```

```typescript
// React frontend — custom SSE hook
const { messages, isLoading, sendMessage } = useDocumentChat();
// Parses SSE stream, accumulates text-delta events, collects citation events
```

Vite's `server.proxy` forwards `/api/*` to the .NET backend in development. In production, .NET serves the built React files from `wwwroot/`.

## Coding Standards

- **C# conventions**: PascalCase public, `_camelCase` private fields
- **TypeScript**: strict mode, functional components, named exports
- **Async all the way**: async/await for I/O, never `.Result` or `.Wait()`
- **Minimal API** for endpoints
- **DTOs at API boundary**: don't expose internal models
- **Configuration**: `appsettings.json` + user secrets for API keys. Never hardcode
- **Structured logging**: `ILogger<T>` with message templates
- **Modern C#**: file-scoped namespaces, primary constructors, collection expressions

## Development Commands

```bash
# Backend
dotnet build AskMyPdf.sln
dotnet run --project src/AskMyPdf.Web/AskMyPdf.Web.csproj
dotnet test AskMyPdf.sln
dotnet test --filter "FullyQualifiedName~TestClassName.TestMethodName"
dotnet format AskMyPdf.sln

# Frontend (from client/ directory)
npm install
npm run dev          # Vite dev server with proxy to .NET
npm run build        # Production build → dist/
```

## Priorities (in order)

1. **Solution scaffold + PDF upload + bounding box extraction + SQLite** — the foundation
2. **Question pipeline: send PDF to Claude API with citations + SSE streaming** — the core feature
3. **React UI: split-pane with chat + PDF viewer with highlighting** — the visual differentiator
4. **Citation → bounding box matching + highlight precision** — the technical differentiator
5. **"I don't know" handling + edge cases** — shows maturity
6. **README + design explanation** — required deliverable, graded
7. **Polish, error handling, streaming UX** — if time allows
8. **Deploy or record demo** — required deliverable

## Workflow

Use Claude commands to stay structured:

1. `/plan` → generate full implementation plan
2. `/prp` → generate task PRP for next piece of work
3. Implement the task
4. `/review` → validate quality and simplicity
5. `/validate` → run build + tests
6. Repeat from step 2

Switch to specialized agents when deep focus is needed:
- `@architect` for design decisions and tradeoffs
- `@backend` for .NET implementation and React integration
- `@rag` for RAG pipeline specifics (chunking, retrieval, prompts, citations)
- `@reviewer` before committing or when code feels complex
