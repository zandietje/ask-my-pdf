# Development Workflow

## Day-by-Day Plan

### Day 1: Foundation + Ingest Pipeline
1. `/plan` → Generate full implementation plan
2. `/prp` → Solution scaffold (.sln, 3 .NET projects, NuGet packages, DI)
3. `/prp` → React scaffold (Vite + TS, shadcn/ui, Tailwind, proxy config)
4. `@backend` → Implement scaffolding for both
5. `/prp` → PDF upload + PdfPig text extraction + bounding boxes + chunking + embedding + storage
6. `@rag` + `@backend` → Implement ingest pipeline
7. `/validate` → Verify build passes

### Day 2: Query Pipeline + RAG
1. `/prp` → Query reformulation + vector search + relevance filtering
2. `@rag` → Implement retrieval with reformulation
3. `/prp` → Claude Citations API integration + SSE streaming endpoint
4. `@rag` + `@backend` → Implement answer generation with structured citations
5. Test end-to-end: upload PDF → ask question → get answer with citations
6. `/review rag` → Validate pipeline quality

### Day 3: React UI + Integration
1. `/prp` → React UI: split-pane layout, chat panel with useChat, PDF viewer with highlighting
2. `@backend` → Implement React components
3. Wire chat citations → PDF viewer jumpToHighlightArea
4. Upload UI with drag-and-drop, document list
5. `/validate` → Full validation
6. `/review approach` → Architecture check

### Day 4: Polish + Deliver
1. `/review demo` → Full assessment readiness review
2. `/simplify` → Remove unnecessary complexity
3. "I don't know" edge cases, error handling, loading states
4. Write README.md (setup instructions, design decisions, architecture explanation)
5. `/validate` → Final validation
6. Record demo video or deploy
7. Final git push

## When to Switch Agents

| Situation | Agent |
|-----------|-------|
| "Should I structure this as X or Y?" | `@architect` |
| "Implement this endpoint/component/service" | `@backend` |
| "Chunking/embedding/retrieval/citation logic" | `@rag` |
| "Is this too complex? Review before commit" | `@reviewer` |

## Decision Framework

When stuck:
1. Does the assessment require it? → If no, skip
2. Can I demo without it? → If yes, defer
3. Will it take more than 30 minutes? → Find simpler approach
4. Am I adding this "just in case"? → Remove it
5. Would an assessor notice this? → If no, skip
