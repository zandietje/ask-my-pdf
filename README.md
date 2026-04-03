# AskMyPdf

A document Q&A application that lets you upload PDFs, ask natural language questions, and get AI-generated answers with precise, clickable citations that highlight the exact source text in the document.

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)
![React 18](https://img.shields.io/badge/React-18-61DAFB?logo=react)
![Claude API](https://img.shields.io/badge/Anthropic-Claude%20API-D4A574)
![License](https://img.shields.io/badge/license-MIT-green)

## How It Works

1. **Upload** a PDF document (up to 32 MB)
2. **Ask** a question about its contents
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
| LLM | Anthropic Claude API (Citations + prompt caching) |
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
| PdfPig for bounding boxes only | Text extraction is Claude's job; PdfPig maps `cited_text` → pixel-perfect highlight coordinates |
| SSE, not WebSocket | Streaming is server-to-client only; SSE is simpler with native Fetch API support |
| SQLite, not Postgres | Single-file database, zero infrastructure; perfect for a self-contained app |
| React 18, not 19 | @react-pdf-viewer compatibility (archived March 2026 with React 18 support) |
| No EF Core | Direct ADO.NET for full control and simplicity |

## Project Structure

```
ask-my-pdf/
├── src/
│   ├── AskMyPdf.Core/              # Domain models (zero dependencies)
│   ├── AskMyPdf.Infrastructure/    # SQLite, PdfPig, Claude API, services
│   └── AskMyPdf.Web/               # Minimal API endpoints + DI
├── client/                          # React 18 + Vite + TypeScript
├── tests/
│   └── AskMyPdf.Tests/             # xUnit + FluentAssertions
├── AskMyPdf.slnx
└── Directory.Build.props
```

## License

MIT
