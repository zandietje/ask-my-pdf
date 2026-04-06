# AskMyPdf

A document Q&A application that lets you upload PDFs and ask natural language questions about their contents. Every answer is grounded in the document with clickable citations that jump to and highlight the exact source passage in the PDF viewer.

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)
![React 18](https://img.shields.io/badge/React-18-61DAFB?logo=react)
![Claude API](https://img.shields.io/badge/Anthropic-Claude_API-D4A574)
![TypeScript](https://img.shields.io/badge/TypeScript-strict-3178C6?logo=typescript)
![SQLite](https://img.shields.io/badge/SQLite-FTS5-003B57?logo=sqlite)
![License](https://img.shields.io/badge/license-MIT-green)

**Live demo:** [askmypdf.duckdns.org](https://askmypdf.duckdns.org)

---

## How It Works

1. **Upload** a PDF document (up to 100 pages, 32 MB)
2. **Ask** a question about its contents — in any language
3. **Read** a streamed answer with inline citation chips like `[Page 7]`
4. **Click** a citation to jump to the exact passage, highlighted word-by-word in the PDF viewer

### Two Analysis Modes

The app offers two answer engines that share a common citation and highlighting pipeline. Switch between them with the toggle in the top bar.

| Mode | How it works | Streaming | Best for |
|------|-------------|-----------|----------|
| **Quick** (RAG) | Hybrid FTS5 + vector search retrieves relevant chunks, Claude generates a grounded answer with inline chunk citations. Small chunks (~150 chars) are precise by design — no post-processing needed. | Real-time | Fast answers, lower API cost |
| **Deep** (Claude CLI) | Runs Claude Code as a subprocess with a structured JSON prompt for multi-pass analysis. Returns exact verbatim snippets with page references. | Batch | Thorough analysis of complex documents |

### What Makes This Different

- **Every claim is cited** — the LLM is prompted to reference specific chunks, and those references are deterministically mapped back to the document
- **Word-level highlighting** — PdfPig extracts bounding boxes at the word level; clicking a citation draws precise yellow overlays on the exact text
- **Hybrid retrieval** — FTS5 (lexical, BM25) and OpenAI embeddings (semantic, cosine similarity) are merged via Reciprocal Rank Fusion, catching both keyword and meaning-based matches
- **Real-time streaming** — answers appear token-by-token via SSE as Claude generates them; citations arrive at the end and are instantly clickable
- **No vector database** — everything runs on a single SQLite file: documents, file blobs, word bounding boxes, text chunks, FTS5 index, and vector embeddings

---

## Architecture

```
┌──────────────────────────────────────────────────────────┐
│            React 18  ·  Vite  ·  TypeScript              │
│   ┌──────────────────┐     ┌──────────────────────────┐  │
│   │   Chat Panel      │     │   PDF Viewer Panel       │  │
│   │   streaming text  │     │   @react-pdf-viewer      │  │
│   │   [Page 7] ───────┼────>│   word-level highlights  │  │
│   └────────┬──────────┘     └──────────────────────────┘  │
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
  (bbox)  ├─ RAG (Quick)    (docs, chunks, FTS5,
          └─ CLI (Deep)      embeddings, bboxes)
```

| Layer | Project | Responsibility |
|-------|---------|----------------|
| API | `AskMyPdf.Web` | HTTP endpoints, DTOs, DI wiring |
| Domain | `AskMyPdf.Core` | Models (`Citation`, `HighlightArea`, `PageToken`, etc.), `IAnswerEngine` interface |
| Infrastructure | `AskMyPdf.Infrastructure` | SQLite repositories, PdfPig extraction, Claude API calls, RAG retrieval, CLI runner, coordinate transforms |
| Frontend | `client/` | React SPA — split-pane chat + PDF viewer, SSE streaming, citation navigation |

---

## Tech Stack

| Component | Technology | Why |
|-----------|------------|-----|
| Backend | .NET 8, C# 12, Minimal API | Required constraint; lightweight, async-native |
| Frontend | React 18, TypeScript (strict), Vite | Best PDF viewer ecosystem; fast dev cycle |
| Styling | Tailwind CSS + shadcn/ui + Framer Motion | Utility-first CSS, accessible components, smooth animations |
| PDF Viewer | @react-pdf-viewer 3.12 + highlight plugin | Programmatic `jumpToHighlightArea`, page navigation |
| LLM | Claude Sonnet 4 via Anthropic SDK | High-quality answers, streaming support |
| RAG Retrieval | SQLite FTS5 (BM25) + OpenAI embeddings | Hybrid lexical + semantic search; graceful degradation without OpenAI key |
| PDF Parsing | PdfPig 0.1.14 | Word-level bounding box extraction with Document Layout Analysis |
| Database | SQLite | Single file, zero infrastructure — stores everything |
| Deployment | Docker + Caddy (HTTPS) | Multi-stage build, automatic TLS |
| Testing | xUnit + FluentAssertions | 84 tests covering extraction, transforms, retrieval, and services |

---

## Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js 18+](https://nodejs.org/)
- [Anthropic API key](https://console.anthropic.com/) (required)
- [OpenAI API key](https://platform.openai.com/) (optional — enables vector search in RAG mode)
- [Claude Code CLI](https://docs.anthropic.com/en/docs/claude-code) (optional — enables the Deep analysis mode)

### Setup

```bash
# Clone
git clone https://github.com/zandietje/ask-my-pdf.git
cd ask-my-pdf

# Set your Anthropic API key (required)
cd src/AskMyPdf.Web
dotnet user-secrets set "Anthropic:ApiKey" "sk-ant-..."

# (Optional) Set OpenAI API key for hybrid vector + FTS5 retrieval
# Without this, RAG mode uses FTS5 lexical search only — still works
dotnet user-secrets set "OpenAI:ApiKey" "sk-..."
cd ../..

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

Open **http://localhost:5173** in your browser.

### Test

```bash
dotnet test AskMyPdf.slnx
```

---

## How the Pipeline Works

### Upload (PDF ingestion)

```
PDF file
  → PdfPig: Document Layout Analysis pipeline
    - NearestNeighbourWordExtractor: groups characters into words
    - RecursiveXYCut: segments page into text blocks (columns, paragraphs)
    - UnsupervisedReadingOrderDetector: determines correct reading sequence
    - Group words into visual lines by Y-proximity
  → Canonical page representation: reading-order text + word tokens with bounding boxes
  → DocumentChunker: split into ~150-char chunks at sentence boundaries
  → Store in SQLite: file bytes, metadata, page bounds, chunks, FTS5 index
  → (Optional) OpenAI embeddings for each chunk → stored as BLOB
```

### Question (streamed via SSE)

```
User question + document ID
  → RAG engine:
    1. FTS5 search (BM25 ranking) + vector search (cosine similarity)
    2. Merge results with Reciprocal Rank Fusion
    3. Expand with adjacent chunks for context
    4. Stream answer from Claude with inline [C3] citations
    5. Extract chunk IDs via regex → map to chunk text
  → For each citation:
    - Load canonical page representation from SQLite
    - Dense normalized substring match: strip whitespace, diacritics, ligatures (NFKD)
    - Map matched characters → token indices → bounding boxes
    - Group tokens by Y-proximity into visual lines
    - Convert PdfPig coords (bottom-left origin) → viewer coords (top-left, percentages)
  → Stream text-delta + enriched citations via SSE to frontend
  → Frontend renders answer + clickable [Page N] chips
  → Click → PDF viewer jumps to page, highlights passage with yellow overlay
```

---

## Design Decisions

| Decision | Rationale |
|----------|-----------|
| **Pluggable engine interface** | `IAnswerEngine` lets both engines produce the same `AnswerStreamEvent` stream. The downstream citation + highlighting pipeline is completely engine-agnostic. Adding a new engine means implementing one interface. |
| **Hybrid RAG with RRF** | FTS5 catches keyword queries ("revenue 2024"); vector search catches semantic queries ("how much money did they make"). Reciprocal Rank Fusion merges both ranked lists without needing to normalize scores. Falls back to FTS5-only without an OpenAI key. |
| **Small chunks (~150 chars)** | Each chunk is 1–2 sentences. The full chunk text IS the citation — no post-processing or "focus pass" needed. Small chunks also mean more precise retrieval. |
| **Canonical page representation** | Reading order is solved once at upload time using PdfPig's DLA pipeline. At query time, citation matching is a simple normalized substring search — no spatial reordering or heuristics. |
| **Dense Unicode normalization** | Cited text is matched against page text after stripping whitespace, lowercasing, removing diacritics, and decomposing ligatures (ﬁ→fi via NFKD). This handles the mismatch between LLM-generated citations and raw PDF text. |
| **SSE, not WebSocket** | Streaming is server-to-client only. SSE is simpler, works with native Fetch API, and auto-reconnects. |
| **SQLite for everything** | Single file, zero infrastructure. Stores documents, file blobs, word bounding boxes, chunks, FTS5 index, and vector embeddings. No Postgres, no Redis, no vector database. |
| **No EF Core** | Direct ADO.NET with parameterized queries. Full control, no magic, fewer dependencies. |
| **React 18, not 19** | @react-pdf-viewer requires React 18 (last updated March 2026). |

---

## Docker Deployment

The app ships with a multi-stage Dockerfile (frontend build → backend publish → runtime with Node.js + Claude CLI) and Caddy for automatic HTTPS.

```bash
# Build and run locally
docker compose up --build

# Production (pre-built image)
ANTHROPIC_API_KEY=sk-ant-... docker compose -f docker-compose.prod.yml up -d
```

### Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `Anthropic__ApiKey` | *(required)* | Anthropic API key |
| `OpenAI__ApiKey` | *(optional)* | Enables vector embeddings for hybrid RAG retrieval |
| `Rag__Enabled` | `true` | Enable/disable RAG engine |
| `Rag__Model` | `claude-sonnet-4-20250514` | Model for answer generation |
| `Rag__TopK` | `8` | Number of chunks to retrieve |
| `ClaudeCli__Enabled` | `true` | Enable/disable CLI engine |
| `ClaudeCli__BinaryPath` | `claude` | Path to Claude Code binary |
| `ClaudeCli__TimeoutSeconds` | `120` | CLI subprocess timeout |
| `ClaudeCli__MaxTurns` | `2` | Max agentic turns for CLI engine |
| `Database__Path` | `askmypdf.db` | SQLite file path |

---

## Project Structure

```
ask-my-pdf/
├── src/
│   ├── AskMyPdf.Core/              # Domain models + IAnswerEngine interface
│   │   ├── Models/                # Document, Citation, HighlightArea, PageToken, etc.
│   │   └── Services/              # IAnswerEngine contract
│   ├── AskMyPdf.Infrastructure/    # All implementations
│   │   ├── Ai/                    # RagAnswerEngine, ClaudeCliEngine, EmbeddingService
│   │   ├── Pdf/                   # BoundingBoxExtractor, CoordinateTransformer, DocumentChunker
│   │   ├── Data/                  # SqliteDb, DocumentRepository, ChunkRepository
│   │   └── Services/              # DocumentService, QuestionService
│   └── AskMyPdf.Web/               # Minimal API — endpoints, DTOs, DI
│       ├── Endpoints/             # DocumentEndpoints, QuestionEndpoints
│       └── Dtos/                  # QuestionRequest, DocumentDto, UploadResponse
├── client/                          # React 18 + Vite + TypeScript
│   └── src/
│       ├── components/
│       │   ├── chat/              # ChatPanel, MessageBubble, CitationChip, markdown rendering
│       │   ├── pdf/               # PdfViewerPanel, HighlightLayer
│       │   ├── upload/            # UploadDropzone, DocumentList
│       │   ├── layout/            # AppLayout (desktop 3-column / mobile tab bar)
│       │   └── ui/                # shadcn/ui + EngineSelector
│       ├── hooks/                 # useDocumentChat (SSE), useDocumentManager, useTheme
│       └── lib/                   # API client, types, utilities
├── tests/
│   └── AskMyPdf.Tests/             # 84 xUnit tests
├── Dockerfile                       # Multi-stage: Node → .NET SDK → runtime
├── docker-compose.yml               # Local dev with Caddy
├── docker-compose.prod.yml          # Production with HTTPS
├── Caddyfile
└── AskMyPdf.slnx
```

---

## API Reference

| Method | Route | Description |
|--------|-------|-------------|
| `GET` | `/health` | Health check |
| `GET` | `/api/engines` | List available answer engines |
| `POST` | `/api/documents/upload` | Upload PDF (multipart/form-data) |
| `GET` | `/api/documents` | List all documents |
| `GET` | `/api/documents/{id}` | Get document metadata |
| `GET` | `/api/documents/{id}/file` | Download PDF file |
| `DELETE` | `/api/documents/{id}` | Delete document and all related data |
| `POST` | `/api/questions` | Stream answer via SSE (`text-delta`, `citation`, `done` events) |

---

## Known Limitations

- **100-page PDF limit** — Claude API context window constraint
- **32 MB file size** — upload limit
- **Single document per session** — questions are scoped to one document at a time
- **LTR text only** — bounding box matching assumes left-to-right scripts (answers support any language)
- **No conversation memory** — each question is independent
- **Deep mode is batch** — full answer appears at once, no token-by-token streaming
- **Vector search requires OpenAI key** — without it, RAG uses FTS5 lexical search only

---

## Troubleshooting

**"Anthropic:ApiKey is required" on startup** — Set the key via user secrets:
```bash
cd src/AskMyPdf.Web && dotnet user-secrets set "Anthropic:ApiKey" "sk-ant-..."
```

**PDF upload fails** — Ensure the file is a valid PDF (not a renamed .docx). The app validates `%PDF` magic bytes.

**Slow first question** — The first question on a new document indexes chunks and warms up the model. Subsequent questions are faster.

**RAG returns "No indexed content found"** — Chunks are created during upload. If you uploaded before RAG was enabled, delete and re-upload.

**Deep mode not available** — Install the Claude Code CLI: `npm install -g @anthropic-ai/claude-code`

---

## License

MIT
