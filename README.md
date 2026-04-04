# AskMyPdf

A document Q&A application that lets you upload PDFs, ask natural language questions, and get AI-generated answers with precise, clickable citations that highlight the exact source text in the document.

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)
![React 18](https://img.shields.io/badge/React-18-61DAFB?logo=react)
![Claude API](https://img.shields.io/badge/Anthropic-Claude%20API-D4A574)
![License](https://img.shields.io/badge/license-MIT-green)

## How It Works

1. **Upload** a PDF document (up to 32 MB)
2. **Choose** an answer engine (Anthropic API or Claude Code CLI)
3. **Ask** a question about its contents (in any language)
4. **Read** a streamed answer with inline citation chips (e.g. `[Page 7]`)
5. **Click** a citation to jump to the exact passage, highlighted word-by-word in the PDF viewer

### Two Answer Engines

The app ships with a **pluggable engine architecture** — both engines produce the same citation + highlight pipeline:

| Engine | How it works | Streaming | Citation source |
|--------|-------------|-----------|-----------------|
| **Anthropic API** (default) | Sends full PDF to Claude API with Citations enabled. Real-time token streaming. Broad citations refined with a parallel Haiku focus pass. | Real-time | Claude Citations API (`page_location`) |
| **Claude Code CLI** | Runs `claude` as a subprocess with a structured JSON prompt. Returns exact snippets from the document. No focus pass needed. | Batch | CLI structured output (`evidence[].snippets`) |

Users switch between engines via a dropdown in the chat panel.

### Why Not RAG?

This project takes a **direct PDF** approach instead of the traditional RAG pipeline (chunk → embed → vector search → generate). The full PDF is sent to Claude with the Citations feature enabled, which means:

- **No chunking, embeddings, or vector store** — dramatically simpler architecture
- **Higher answer quality** — the model sees the full document context, not isolated chunks
- **Precise citations** — Claude returns structured `cited_text` with page numbers, not hallucinated references
- **Prompt caching** — repeat questions on the same document cost ~90% less

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
  (bbox)  ├─ Anthropic API  (data)
          └─ Claude CLI
```

| Layer | Project | Responsibility |
|-------|---------|----------------|
| API | `AskMyPdf.Web` | HTTP endpoints, DTOs, middleware |
| Domain | `AskMyPdf.Core` | Models, `IAnswerEngine` interface |
| Infrastructure | `AskMyPdf.Infrastructure` | SQLite, PdfPig, Claude API, CLI runner, coordinate transforms |
| Frontend | `client/` | React SPA with split-pane chat + PDF viewer + engine selector |

## Tech Stack

| Component | Technology |
|-----------|------------|
| Backend | .NET 8, C# 12, Minimal API |
| Frontend | React 18, TypeScript (strict), Vite |
| Styling | Tailwind CSS, shadcn/ui |
| PDF Viewer | @react-pdf-viewer/core + highlight plugin |
| LLM (Engine A) | Anthropic Claude API (Sonnet for answers, Haiku for citation focus, prompt caching) |
| LLM (Engine B) | Claude Code CLI (subprocess, structured JSON output) |
| PDF Parsing | PdfPig (word-level bounding box extraction) |
| Database | SQLite (documents, file blobs, bounding boxes) |
| Deployment | Docker, Caddy (HTTPS reverse proxy) |
| Testing | xUnit, FluentAssertions (45 tests) |

## Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js 18+](https://nodejs.org/)
- [Anthropic API key](https://console.anthropic.com/)
- [Claude Code CLI](https://docs.anthropic.com/en/docs/claude-code) (optional, for Engine B)

### Setup

```bash
# Clone
git clone https://github.com/zandietje/ask-my-pdf.git
cd ask-my-pdf

# Set your Anthropic API key
cd src/AskMyPdf.Web
dotnet user-secrets set "Anthropic:ApiKey" "sk-ant-..."
cd ../..

# (Optional) Override models in appsettings.json
# Anthropic:AnswerModel — model for answering questions (default: claude-sonnet-4-20250514)
# Anthropic:FocusModel  — model for citation extraction (default: claude-haiku-4-5-20251001)

# (Optional) Install Claude Code CLI for Engine B
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

### Test

```bash
dotnet test AskMyPdf.slnx
```

## Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| Direct PDF, not RAG | Full-context answers outperform chunk-based retrieval; Citations API provides structured source references |
| Pluggable engine architecture | `IAnswerEngine` lets both Anthropic API and Claude CLI produce the same event stream, sharing the downstream citation + highlighting pipeline |
| Two-phase citation focusing (Engine A) | Claude's Citations API often over-cites (whole paragraphs/sections). A second, parallel pass with Haiku extracts just the supporting sentences, producing precise word-level highlights |
| Parallel focus calls + Haiku | Focus calls are independent per page — `Task.WhenAll` fires them concurrently. Using Haiku (not Sonnet) for this extractive task is ~10-20x cheaper and faster |
| No focus pass for CLI (Engine B) | CLI prompt requires exact verbatim snippets, so citations are already precise. Skips the Haiku focus step entirely |
| PdfPig for bounding boxes only | Text extraction is Claude's job; PdfPig maps `cited_text` → pixel-perfect highlight coordinates |
| SSE, not WebSocket | Streaming is server-to-client only; SSE is simpler with native Fetch API support |
| SQLite, not Postgres | Single-file database, zero infrastructure; perfect for a self-contained app |
| React 18, not 19 | @react-pdf-viewer compatibility (archived March 2026 with React 18 support) |
| No EF Core | Direct ADO.NET for full control and simplicity |

## Docker Deployment

The app ships with a multi-stage Dockerfile and Caddy reverse proxy:

```bash
# Build and run locally
docker compose up --build

# Production (uses pre-built image from GHCR)
ANTHROPIC_API_KEY=sk-ant-... docker compose -f docker-compose.prod.yml up -d
```

The Docker image includes Node.js and the Claude Code CLI, so both engines work out of the box.

### Configuration (environment variables)

| Variable | Default | Description |
|----------|---------|-------------|
| `Anthropic__ApiKey` | (required) | Anthropic API key |
| `Anthropic__AnswerModel` | `claude-sonnet-4-20250514` | Model for Q&A answers |
| `Anthropic__FocusModel` | `claude-haiku-4-5-20251001` | Model for citation focusing |
| `ClaudeCli__Enabled` | `true` | Enable/disable CLI engine |
| `ClaudeCli__BinaryPath` | `claude` | Path to Claude CLI binary |
| `ClaudeCli__TimeoutSeconds` | `120` | CLI subprocess timeout |
| `Database__Path` | `askmypdf.db` | SQLite database file path |

## Project Structure

```
ask-my-pdf/
├── src/
│   ├── AskMyPdf.Core/              # Domain models, IAnswerEngine interface
│   ├── AskMyPdf.Infrastructure/    # SQLite, PdfPig, Claude API, CLI runner, services
│   └── AskMyPdf.Web/               # Minimal API endpoints + DI + static files
├── client/                          # React 18 + Vite + TypeScript
├── tests/
│   └── AskMyPdf.Tests/             # xUnit + FluentAssertions (45 tests)
├── Dockerfile                       # Multi-stage build (frontend + backend + CLI)
├── docker-compose.yml               # Local development
├── docker-compose.prod.yml          # Production with Caddy HTTPS
├── AskMyPdf.slnx
└── Directory.Build.props
```

## Known Limitations

- **100-page PDF limit** — Claude API context window constraint
- **32 MB file size limit** — Claude API upload constraint
- **Single document per session** — questions are scoped to one document at a time
- **Left-to-right text only** — bounding box matching assumes LTR scripts (answers support any language)
- **No conversation memory** — each question is independent (no multi-turn context)
- **CLI engine is batch, not streaming** — the full answer appears at once (no token-by-token streaming)

## Troubleshooting

### "Anthropic:ApiKey is required" on startup

Ensure you've set the API key via user secrets:

```bash
cd src/AskMyPdf.Web
dotnet user-secrets set "Anthropic:ApiKey" "sk-ant-..."
```

### PDF upload fails with "could not be processed"

Ensure the file is a valid PDF (not a renamed .docx or image). The app validates PDF magic bytes (`%PDF` header).

### Slow first question on a document

The first question sends the full PDF to Claude and initializes prompt caching. Subsequent questions on the same document are ~90% cheaper and faster due to cache hits.

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
