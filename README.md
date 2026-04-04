# AskMyPdf

A document Q&A application that lets you upload PDFs, ask natural language questions, and get AI-generated answers with precise, clickable citations that highlight the exact source text in the document.

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)
![React 18](https://img.shields.io/badge/React-18-61DAFB?logo=react)
![Claude API](https://img.shields.io/badge/Anthropic-Claude%20API-D4A574)
![License](https://img.shields.io/badge/license-MIT-green)

## How It Works

1. **Upload** a PDF document (up to 32 MB)
2. **Ask** a question about its contents (in any language)
3. **Read** a streamed answer with inline citation chips (e.g. `[Page 7]`)
4. **Click** a citation to jump to the exact passage, highlighted word-by-word in the PDF viewer

### Why Not RAG?

This project takes a **direct PDF** approach instead of the traditional RAG pipeline (chunk → embed → vector search → generate). The full PDF is sent to Claude's API with the Citations feature enabled, which means:

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
│   │   streaming text │     │   @react-pdf-viewer      │  │
│   │   [Page 7] ──────┼────>│   word-level highlights  │  │
│   └────────┬─────────┘     └──────────────────────────┘  │
│            │ SSE                                          │
└────────────┼─────────────────────────────────────────────┘
             │
┌────────────▼─────────────────────────────────────────────┐
│            .NET 8 Minimal API                             │
│   POST /api/documents/upload   POST /api/questions (SSE)  │
│   GET  /api/documents          GET  /api/documents/:id    │
└────────────┬─────────────────────────────────────────────┘
             │
     ┌───────┼───────┐
     │       │       │
  PdfPig  Claude  SQLite
  (bbox)  (LLM)   (data)
```

| Layer | Project | Responsibility |
|-------|---------|----------------|
| API | `AskMyPdf.Web` | HTTP endpoints, DTOs, middleware |
| Domain | `AskMyPdf.Core` | Models (`Document`, `Citation`, `HighlightArea`, `AnswerStreamEvent`) |
| Infrastructure | `AskMyPdf.Infrastructure` | SQLite, PdfPig extraction, Claude API, coordinate transforms |
| Frontend | `client/` | React SPA with split-pane chat + PDF viewer |

## Tech Stack

| Component | Technology |
|-----------|------------|
| Backend | .NET 8, C# 12, Minimal API |
| Frontend | React 18, TypeScript (strict), Vite |
| Styling | Tailwind CSS, shadcn/ui |
| PDF Viewer | @react-pdf-viewer/core + highlight plugin |
| LLM | Anthropic Claude API (Sonnet for answers, Haiku for citation focus, prompt caching) |
| PDF Parsing | PdfPig (word-level bounding box extraction) |
| Database | SQLite (documents, file blobs, bounding boxes) |
| Testing | xUnit, FluentAssertions |

## Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js 18+](https://nodejs.org/)
- [Anthropic API key](https://console.anthropic.com/)

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
| Two-phase citation focusing | Claude's Citations API often over-cites (whole paragraphs/sections). A second, parallel pass with Haiku extracts just the supporting sentences, producing precise word-level highlights |
| Parallel focus calls + Haiku | Focus calls are independent per page — `Task.WhenAll` fires them concurrently. Using Haiku (not Sonnet) for this extractive task is ~10-20x cheaper and faster |
| PdfPig for bounding boxes only | Text extraction is Claude's job; PdfPig maps `cited_text` → pixel-perfect highlight coordinates |
| SSE, not WebSocket | Streaming is server-to-client only; SSE is simpler with native Fetch API support |
| SQLite, not Postgres | Single-file database, zero infrastructure; perfect for a self-contained app |
| React 18, not 19 | @react-pdf-viewer compatibility (archived March 2026 with React 18 support) |
| No EF Core | Direct ADO.NET for full control and simplicity |

## Production Build

For a single-process deployment (no separate frontend dev server):

```bash
# 1. Build the frontend
cd client && npm run build && cd ..

# 2. Copy built files to .NET wwwroot
cp -r client/dist/* src/AskMyPdf.Web/wwwroot/

# 3. Run (serves both API + frontend on one port)
dotnet run --project src/AskMyPdf.Web -c Release
```

Navigate to `http://localhost:5000` — the .NET app serves both the API and the React SPA.

## Project Structure

```
ask-my-pdf/
├── src/
│   ├── AskMyPdf.Core/              # Domain models (zero dependencies)
│   ├── AskMyPdf.Infrastructure/    # SQLite, PdfPig, Claude API, services
│   └── AskMyPdf.Web/               # Minimal API endpoints + DI + static files
├── client/                          # React 18 + Vite + TypeScript
├── tests/
│   └── AskMyPdf.Tests/             # xUnit + FluentAssertions
├── AskMyPdf.slnx
└── Directory.Build.props
```

## Known Limitations

- **100-page PDF limit** — Claude API context window constraint
- **32 MB file size limit** — Claude API upload constraint
- **Single document per session** — questions are scoped to one document at a time
- **Left-to-right text only** — bounding box matching assumes LTR scripts (answers support any language)
- **No conversation memory** — each question is independent (no multi-turn context)

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

### Frontend not loading in production mode

Ensure you've built the frontend and copied the output:

```bash
cd client && npm run build
cp -r dist/* ../src/AskMyPdf.Web/wwwroot/
```

## License

MIT
