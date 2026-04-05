# AskMyPdf

A document Q&A application that lets you upload PDFs, ask natural language questions, and get AI-generated answers with precise, clickable citations that highlight the exact source text in the document.

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)
![React 18](https://img.shields.io/badge/React-18-61DAFB?logo=react)
![Claude API](https://img.shields.io/badge/Anthropic-Claude%20API-D4A574)
![License](https://img.shields.io/badge/license-MIT-green)

## How It Works

1. **Upload** a PDF document (up to 100 pages, 32 MB)
2. **Choose** an analysis mode — RAG (Hybrid), Anthropic API, or Claude Code CLI
3. **Ask** a question about its contents (in any language)
4. **Read** a streamed answer with inline citation chips (e.g. `[Page 7]`)
5. **Click** a citation to jump to the exact passage, highlighted word-by-word in the PDF viewer

### Three Answer Engines

The app ships with a **pluggable engine architecture** — all three engines feed into the same citation + highlight pipeline:

| Engine | How it works | Streaming | Best for |
|--------|-------------|-----------|----------|
| **RAG (Hybrid)** | Hybrid FTS5 + vector search retrieves relevant chunks, Claude generates a grounded answer with inline `[C3]` chunk citations. No focus pass needed — small chunks (~150 chars) are precise by design. | Real-time | Fast answers, lower API cost |
| **Anthropic API** | Sends the full PDF to Claude with the Citations API enabled. Real-time token streaming. Broad citations refined with a parallel Haiku focus pass for word-level precision. | Real-time | Highest answer quality, cross-section questions |
| **Claude Code CLI** | Runs `claude` as a subprocess with a structured JSON prompt. Returns exact verbatim snippets. No focus pass needed. | Batch | Thorough multi-pass analysis |

Users switch between engines via a dropdown in the chat panel.

### Design Philosophy: Multiple Engines, One Pipeline

Rather than committing to a single approach, the app offers three engines that share a common downstream pipeline (citation resolution + bounding box matching + PDF highlighting). This lets users pick the right tradeoff for their task:

- **RAG (Hybrid)** is the fastest and cheapest — it only sends retrieved chunks to Claude, not the full PDF. Hybrid retrieval (FTS5 lexical search + OpenAI vector embeddings merged via reciprocal rank fusion) handles both keyword and semantic queries. Falls back to FTS5-only if no OpenAI key is configured.
- **Anthropic API** produces the highest-quality answers because Claude sees the entire document context. The Citations API returns structured `page_location` references with exact `cited_text`, guaranteed to be real (extracted, not generated). A parallel Haiku focus pass narrows broad citations to precise supporting sentences.
- **Claude Code CLI** gives the deepest analysis by running Claude Code as a subprocess with multi-turn reasoning. Returns structured evidence with verbatim snippets.

## Architecture

```
┌──────────────────────────────────────────────────────────┐
│            React 18  ·  Vite  ·  TypeScript              │
│   ┌──────────────────┐     ┌──────────────────────────┐  │
│   │   Chat Panel     │     │   PDF Viewer Panel       │  │
│   │   engine selector│     │   @react-pdf-viewer      │  │
│   │   streaming text │     │   word-level highlights  │  │
│   │   [Page 7] ──────┼────>│   jumpToHighlightArea    │  │
│   └────────┬─────────┘     └──────────────────────────┘  │
│            │ SSE                                          │
└────────────┼─────────────────────────────────────────────┘
             │
┌────────────▼─────────────────────────────────────────────┐
│            .NET 8 Minimal API                             │
│   POST /api/documents/upload   POST /api/questions (SSE)  │
│   GET  /api/documents          GET  /api/engines          │
└────────────┬─────────────────────────────────────────────┘
             │
     ┌───────┼───────────────┐
     │       │               │
  PdfPig  IAnswerEngine    SQLite
  (bbox)  ├─ RAG (Hybrid)   (data + chunks
          ├─ Anthropic API    + embeddings)
          └─ Claude CLI
```

| Layer | Project | Responsibility |
|-------|---------|----------------|
| API | `AskMyPdf.Web` | HTTP endpoints, DTOs, DI configuration |
| Domain | `AskMyPdf.Core` | Models, `IAnswerEngine` interface |
| Infrastructure | `AskMyPdf.Infrastructure` | SQLite, PdfPig, Claude API, RAG retrieval, CLI runner, coordinate transforms |
| Frontend | `client/` | React SPA with split-pane chat + PDF viewer + engine selector |

## Tech Stack

| Component | Technology |
|-----------|------------|
| Backend | .NET 8, C# 12, Minimal API |
| Frontend | React 18, TypeScript (strict), Vite |
| Styling | Tailwind CSS, shadcn/ui |
| PDF Viewer | @react-pdf-viewer/core + highlight plugin |
| LLM | Anthropic Claude API (Sonnet for answers, Haiku for citation focus) |
| RAG Retrieval | SQLite FTS5 (BM25) + OpenAI embeddings (cosine similarity), reciprocal rank fusion |
| CLI Engine | Claude Code CLI (subprocess, structured JSON output) |
| PDF Parsing | PdfPig (word-level bounding box extraction) |
| Database | SQLite (documents, file blobs, bounding boxes, chunks, embeddings, FTS5 index) |
| Deployment | Docker, Caddy (HTTPS reverse proxy) |
| Testing | xUnit, FluentAssertions (65 tests) |

## Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js 18+](https://nodejs.org/)
- [Anthropic API key](https://console.anthropic.com/) (required)
- [OpenAI API key](https://platform.openai.com/) (optional — enables vector search in RAG engine)
- [Claude Code CLI](https://docs.anthropic.com/en/docs/claude-code) (optional — enables the CLI engine)

### Setup

```bash
# Clone
git clone https://github.com/zandietje/ask-my-pdf.git
cd ask-my-pdf

# Set your Anthropic API key (required)
cd src/AskMyPdf.Web
dotnet user-secrets set "Anthropic:ApiKey" "sk-ant-..."

# (Optional) Set OpenAI API key for hybrid vector + FTS5 retrieval in RAG engine
# Without this, RAG falls back to FTS5-only (still works, just lexical matching only)
dotnet user-secrets set "OpenAI:ApiKey" "sk-..."
cd ../..

# (Optional) Install Claude Code CLI for the CLI engine
npm install -g @anthropic-ai/claude-code

# Install frontend dependencies
cd client && npm install && cd ..

# Build
dotnet build AskMyPdf.slnx
```

### Run

```bash
# Terminal 1 — Backend (http://localhost:5000)
dotnet run --project src/AskMyPdf.Web

# Terminal 2 — Frontend (http://localhost:5173, proxies /api → :5000)
cd client && npm run dev
```

Open http://localhost:5173 in your browser.

### Test

```bash
dotnet test AskMyPdf.slnx
```

## Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| Three pluggable engines | `IAnswerEngine` interface lets RAG, Anthropic API, and Claude CLI all produce the same `AnswerStreamEvent` stream, sharing the downstream citation + highlighting pipeline. Users pick the right speed/quality/cost tradeoff. |
| Hybrid RAG retrieval (FTS5 + vectors) | FTS5 handles keyword queries; vector search handles semantic queries. Reciprocal rank fusion merges both ranked lists. Graceful degradation: works without OpenAI key (FTS5-only). |
| Small chunks (~150 chars) with context expansion | Small chunks act as precise citation units — the full chunk text IS the citation, so no focus pass is needed. Adjacent chunks are included at query time for surrounding context. |
| Two-phase citation focusing (Anthropic API engine) | Claude's Citations API often over-cites (whole paragraphs/sections). A second, parallel pass with Haiku extracts just the supporting sentences, producing precise word-level highlights. |
| Parallel Haiku focus calls | Focus calls are independent per page — `Task.WhenAll` fires them concurrently. Haiku is ~10-20x cheaper and faster than Sonnet for this extractive task. |
| Direct PDF for Anthropic API engine | Full-context answers outperform chunk-based retrieval on cross-section questions. Prompt caching gives ~90% cost savings on repeat questions. |
| PdfPig for bounding boxes only | Claude handles text understanding; PdfPig maps `cited_text` → pixel-perfect highlight coordinates via dense character matching with Unicode normalization. |
| SSE, not WebSocket | Streaming is server-to-client only; SSE is simpler with native Fetch API support. |
| SQLite, not Postgres | Single-file database, zero infrastructure. Stores documents, file blobs, word bounding boxes, RAG chunks, FTS5 index, and vector embeddings. |
| React 18, not 19 | @react-pdf-viewer compatibility (archived March 2026 with React 18 support). |
| No EF Core | Direct ADO.NET with parameterized queries for full control and simplicity. |

## Docker Deployment

The app ships with a multi-stage Dockerfile and Caddy reverse proxy:

```bash
# Build and run locally
docker compose up --build

# Production (uses pre-built image from GHCR)
ANTHROPIC_API_KEY=sk-ant-... docker compose -f docker-compose.prod.yml up -d
```

The Docker image includes Node.js and the Claude Code CLI, so all three engines work out of the box.

### Configuration (environment variables)

| Variable | Default | Description |
|----------|---------|-------------|
| `Anthropic__ApiKey` | *(required)* | Anthropic API key |
| `Anthropic__AnswerModel` | `claude-sonnet-4-20250514` | Model for Q&A answers |
| `Anthropic__FocusModel` | `claude-haiku-4-5-20251001` | Model for citation focusing |
| `OpenAI__ApiKey` | *(optional)* | OpenAI API key for vector embeddings (enables hybrid RAG retrieval) |
| `OpenAI__EmbeddingModel` | `text-embedding-3-small` | OpenAI embedding model |
| `OpenAI__Dimensions` | `1536` | Embedding vector dimensions |
| `Rag__Enabled` | `true` | Enable/disable RAG engine |
| `Rag__Model` | `claude-sonnet-4-20250514` | Model for RAG answer generation |
| `Rag__TopK` | `8` | Number of chunks to retrieve |
| `ClaudeCli__Enabled` | `true` | Enable/disable CLI engine |
| `ClaudeCli__BinaryPath` | `claude` | Path to Claude CLI binary |
| `ClaudeCli__TimeoutSeconds` | `120` | CLI subprocess timeout |
| `Database__Path` | `askmypdf.db` | SQLite database file path |

## Project Structure

```
ask-my-pdf/
├── src/
│   ├── AskMyPdf.Core/              # Domain models, IAnswerEngine interface
│   │   ├── Models/                # Document, Citation, HighlightArea, WordBoundingBox, etc.
│   │   └── Services/              # IAnswerEngine, DocumentService, QuestionService
│   ├── AskMyPdf.Infrastructure/    # SQLite, PdfPig, Claude API, RAG, CLI runner
│   │   ├── Ai/                    # AnthropicAnswerEngine, RagAnswerEngine, ClaudeCliEngine
│   │   ├── Pdf/                   # BoundingBoxExtractor, CoordinateTransformer, DocumentChunker
│   │   ├── Data/                  # SqliteDb (documents, chunks, embeddings, FTS5)
│   │   └── Services/              # DocumentService, QuestionService implementations
│   └── AskMyPdf.Web/               # Minimal API endpoints + DI + static files
│       ├── Endpoints/             # DocumentEndpoints, QuestionEndpoints
│       └── Dtos/                  # QuestionRequest, DocumentDto, UploadResponse
├── client/                          # React 18 + Vite + TypeScript
│   └── src/
│       ├── components/
│       │   ├── chat/              # ChatPanel, MessageBubble, CitationChip
│       │   ├── pdf/               # PdfViewerPanel, HighlightLayer
│       │   ├── upload/            # UploadDropzone, DocumentList
│       │   ├── layout/            # AppLayout, MobileHeader, MobileTabBar
│       │   └── ui/                # Button, Input, Sheet, Skeleton, EngineSelector
│       ├── hooks/                 # useDocumentChat (SSE), useIsMobile, useMediaQuery
│       └── lib/                   # api.ts, types.ts
├── tests/
│   └── AskMyPdf.Tests/             # xUnit + FluentAssertions (65 tests)
├── Dockerfile                       # Multi-stage build (frontend + backend + CLI)
├── docker-compose.yml               # Local development
├── docker-compose.prod.yml          # Production with Caddy HTTPS
├── Caddyfile                        # Caddy reverse proxy config
├── AskMyPdf.slnx
└── Directory.Build.props
```

## Known Limitations

- **100-page PDF limit** — Claude API context window constraint
- **32 MB file size limit** — API upload constraint
- **Single document per session** — questions are scoped to one document at a time
- **Left-to-right text only** — bounding box matching assumes LTR scripts (answers support any language)
- **No conversation memory** — each question is independent (no multi-turn context)
- **CLI engine is batch, not streaming** — the full answer appears at once (no token-by-token streaming)
- **Vector search requires OpenAI key** — without it, RAG engine uses FTS5 lexical search only

## Troubleshooting

### "Anthropic:ApiKey is required" on startup

Ensure you've set the API key via user secrets:

```bash
cd src/AskMyPdf.Web
dotnet user-secrets set "Anthropic:ApiKey" "sk-ant-..."
```

### PDF upload fails with "could not be processed"

Ensure the file is a valid PDF (not a renamed .docx or image). The app validates PDF magic bytes (`%PDF` header).

### Slow first question on a document (Anthropic API engine)

The first question sends the full PDF to Claude and initializes prompt caching. Subsequent questions on the same document are ~90% cheaper and faster due to cache hits.

### RAG engine returns "No indexed content found"

Document chunks are created during upload. If you uploaded a document before RAG was enabled, delete and re-upload it to generate chunks.

### Claude Code CLI engine not available

Ensure the CLI is installed and on PATH:

```bash
npm install -g @anthropic-ai/claude-code
claude --version
```

If running in Docker, the CLI is pre-installed in the image.

### Frontend not loading in production mode

Ensure you've built the frontend and copied the output:

```bash
cd client && npm run build
cp -r dist/* ../src/AskMyPdf.Web/wwwroot/
```

## License

MIT
