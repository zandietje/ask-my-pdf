# PRP: Phase 6 — Polish + Documentation

## Objective
Make the app production-ready and assessment-ready — add static file serving, global error handling, loading/empty state polish, input validation hardening, and finalize the README. This is the last phase before demo.

## Scope
**In scope:**
- Static file serving for production (.NET serves built React files from `wwwroot/`)
- Global exception handling middleware (backend returns structured errors, not stack traces)
- Frontend error boundary (catch component crashes, show recovery UI)
- Loading state polish (document list skeleton, PDF viewer loading indicator)
- Input validation hardening (PDF magic bytes check on backend)
- Edge case handling (empty PDF, >100 page PDF, Claude API errors, network failures)
- Toast/notification for upload success
- README finalization (screenshot placeholder, troubleshooting section, deployment notes)
- Production build script (copy `client/dist/` → `src/AskMyPdf.Web/wwwroot/`)
- Final `dotnet build` + `dotnet test` + `npm run build` pass

**Out of scope:**
- Dark mode, accessibility audit, WCAG compliance
- Docker/CI-CD pipeline
- Additional tests (integration, E2E)
- Performance optimization (lazy loading, virtualization)
- CORS headers (same-origin in production)
- Serilog or structured logging overhaul (default ILogger is fine for assessment)

## Prerequisites
- Phases 1-5 complete: full working app with upload, Q&A, streaming, PDF viewer, and citation highlighting
- All endpoints operational: `/api/documents/upload`, `/api/documents`, `/api/documents/{id}`, `/api/documents/{id}/file`, `/api/questions`
- Frontend builds cleanly with `npm run build`
- Backend builds with `dotnet build AskMyPdf.sln`

## Outputs
When done:
- `dotnet run` serves both API endpoints AND the React frontend from a single process
- Unhandled exceptions return `{ error: "..." }` JSON, not 500 with stack traces
- Frontend catches component crashes with an error boundary
- Document list shows a skeleton loader while fetching
- PDF viewer shows a loading spinner while rendering
- Upload success shows a brief toast notification
- Backend rejects non-PDF files (magic bytes check), >100 page PDFs, and >32MB files with clear messages
- README is clone-and-run ready with architecture diagram, setup steps, and design decisions
- All builds + tests pass

## Files to Create/Modify

### New Files
| File | Action | Description |
|------|--------|-------------|
| `client/src/components/ui/error-boundary.tsx` | Create | React error boundary — catches component crashes, shows retry UI |
| `client/src/components/ui/toast.tsx` | Create | Minimal toast notification component (or use shadcn/ui Sonner) |

### Modified Files
| File | Action | Description |
|------|--------|-------------|
| `src/AskMyPdf.Web/Program.cs` | Modify | Add UseDefaultFiles, UseStaticFiles, MapFallbackToFile, global exception handler middleware |
| `src/AskMyPdf.Web/Endpoints/DocumentEndpoints.cs` | Modify | Add PDF magic bytes validation, page count limit (100), structured error for PdfPig failures |
| `src/AskMyPdf.Web/Endpoints/QuestionEndpoints.cs` | Modify | Wrap SSE stream in try/catch, emit error SSE event on Claude API failure |
| `src/AskMyPdf.Web/AskMyPdf.Web.csproj` | Modify | Add wwwroot content include if needed |
| `client/src/App.tsx` | Modify | Wrap app in ErrorBoundary |
| `client/src/components/upload/UploadDropzone.tsx` | Modify | Show toast on upload success |
| `client/src/components/upload/DocumentList.tsx` | Modify | Add skeleton loader during initial fetch |
| `client/src/components/pdf/PdfViewerPanel.tsx` | Modify | Add loading indicator, handle PDF load errors |
| `client/src/hooks/useDocumentChat.ts` | Modify | Handle SSE error events from backend, show meaningful error messages |
| `README.md` | Modify | Add troubleshooting section, known limitations, screenshot placeholder |

## NuGet Packages Needed
None — all packages already installed.

## NPM Packages Needed
| Package | Version | Status |
|---------|---------|--------|
| `sonner` | latest | New — lightweight toast library (pairs with shadcn/ui) |

If `sonner` is too heavy, implement a minimal toast with a `useState` + `setTimeout` pattern (~20 lines).

## Implementation Steps

### Step 1: Static File Serving — `Program.cs`

Add production static file serving so .NET serves the built React app. This is **critical** — without it, the production app only serves API routes.

```csharp
// After var app = builder.Build();, before mapping endpoints:

// Serve static files from wwwroot/ (built React app)
app.UseDefaultFiles();
app.UseStaticFiles();

// ... map API endpoints ...

// SPA fallback: any non-API, non-file route → index.html
app.MapFallbackToFile("index.html");
```

**Build pipeline**: After `npm run build`, copy `client/dist/*` → `src/AskMyPdf.Web/wwwroot/`. Add a note in README for this step. Optionally add a build script:

```bash
# build.sh (or document in README)
cd client && npm run build && cd ..
cp -r client/dist/* src/AskMyPdf.Web/wwwroot/
dotnet run --project src/AskMyPdf.Web
```

Ensure `wwwroot/` directory exists in the project (create it empty with a `.gitkeep` if needed).

### Step 2: Global Exception Handler Middleware — `Program.cs`

Add `UseExceptionHandler` so unhandled exceptions return structured JSON, not stack traces:

```csharp
// In production, return structured error JSON
app.UseExceptionHandler(exApp =>
{
    exApp.Run(async context =>
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        var error = context.Features.Get<IExceptionHandlerFeature>();
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogError(error?.Error, "Unhandled exception");
        await context.Response.WriteAsJsonAsync(new { error = "An unexpected error occurred. Please try again." });
    });
});
```

Place this BEFORE `UseDefaultFiles`/`UseStaticFiles` so it catches exceptions in the pipeline.

### Step 3: PDF Validation Hardening — `DocumentEndpoints.cs`

Add magic bytes check and page count validation:

```csharp
// PDF magic bytes: first 4 bytes are "%PDF"
private static bool IsPdfFile(Stream stream)
{
    var buffer = new byte[4];
    var bytesRead = stream.Read(buffer, 0, 4);
    stream.Position = 0; // Reset for further processing
    return bytesRead == 4 && buffer[0] == 0x25 && buffer[1] == 0x50
        && buffer[2] == 0x44 && buffer[3] == 0x46; // %PDF
}
```

After bounding box extraction, check page count:
```csharp
if (pages.Count > 100)
    return Results.BadRequest(new { error = "PDF exceeds the 100-page limit. Please upload a shorter document." });
```

Wrap the PdfPig extraction in try/catch:
```csharp
try
{
    var pages = boundingBoxExtractor.ExtractWordBounds(stream);
}
catch (Exception ex)
{
    logger.LogWarning(ex, "Failed to process PDF: {FileName}", file.FileName);
    return Results.BadRequest(new { error = "The uploaded file could not be processed. Please ensure it is a valid PDF." });
}
```

### Step 4: Question Endpoint Error Handling — `QuestionEndpoints.cs`

Wrap the SSE streaming loop in try/catch to handle Claude API failures gracefully:

```csharp
try
{
    await foreach (var evt in questionService.StreamAnswerAsync(request.Question, request.DocumentId, ct))
    {
        // ... write SSE events ...
    }
}
catch (Exception ex)
{
    logger.LogError(ex, "Error streaming answer for document {DocumentId}", request.DocumentId);
    // Send error event so frontend can show a message
    await response.WriteAsync($"event: error\ndata: {{\"error\":\"An error occurred while generating the answer. Please try again.\"}}\n\n", ct);
    await response.Body.FlushAsync(ct);
}
```

### Step 5: Frontend Error Boundary — `error-boundary.tsx`

```typescript
import { Component, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { AlertTriangle } from "lucide-react";

interface Props { children: ReactNode; }
interface State { hasError: boolean; }

export class ErrorBoundary extends Component<Props, State> {
  state: State = { hasError: false };

  static getDerivedStateFromError(): State {
    return { hasError: true };
  }

  render() {
    if (this.state.hasError) {
      return (
        <div className="flex h-full items-center justify-center">
          <div className="text-center space-y-4">
            <AlertTriangle className="mx-auto h-12 w-12 text-destructive" />
            <h2 className="text-lg font-semibold">Something went wrong</h2>
            <Button onClick={() => this.setState({ hasError: false })}>
              Try Again
            </Button>
          </div>
        </div>
      );
    }
    return this.props.children;
  }
}
```

Wrap the app in `App.tsx` or `main.tsx`:
```typescript
<ErrorBoundary>
  <App />
</ErrorBoundary>
```

### Step 6: Document List Skeleton — `DocumentList.tsx`

Add a loading state with skeleton placeholders while documents are being fetched:

```typescript
// If documents haven't loaded yet (initial state):
if (isLoading) {
  return (
    <div className="space-y-2 p-2">
      {[1, 2, 3].map(i => (
        <div key={i} className="h-12 animate-pulse rounded-md bg-muted" />
      ))}
    </div>
  );
}
```

This requires passing an `isLoading` prop from the parent or managing the fetch state internally. Currently `App.tsx` calls `getDocuments()` in a `useEffect` — add a `loading` state:

```typescript
const [documentsLoading, setDocumentsLoading] = useState(true);

useEffect(() => {
  getDocuments()
    .then(setDocuments)
    .catch(console.error)
    .finally(() => setDocumentsLoading(false));
}, []);
```

### Step 7: PDF Viewer Loading + Error State — `PdfViewerPanel.tsx`

The `@react-pdf-viewer/core` `Viewer` component fires `onDocumentLoad` when the PDF is ready. Show a spinner until then:

```typescript
const [isLoading, setIsLoading] = useState(true);
const [loadError, setLoadError] = useState(false);

// In Viewer:
<Viewer
  fileUrl={fileUrl}
  plugins={[highlightPluginInstance]}
  onDocumentLoad={() => setIsLoading(false)}
  renderError={() => {
    setLoadError(true);
    return (
      <div className="flex h-full items-center justify-center text-destructive">
        <p>Failed to load PDF. The file may be corrupted.</p>
      </div>
    );
  }}
/>

// Overlay loading spinner:
{isLoading && !loadError && (
  <div className="absolute inset-0 flex items-center justify-center bg-background/80">
    <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
  </div>
)}
```

Reset `isLoading` to `true` when `documentId` changes.

### Step 8: SSE Error Event Handling — `useDocumentChat.ts`

Handle the new `error` SSE event from the backend:

```typescript
case "error": {
  const errorData = JSON.parse(data);
  // Add error text to the assistant message
  setMessages(prev => {
    const updated = [...prev];
    const last = updated[updated.length - 1];
    if (last?.role === "assistant") {
      updated[updated.length - 1] = {
        ...last,
        text: last.text + "\n\n⚠️ " + (errorData.error || "An error occurred."),
        isStreaming: false,
      };
    }
    return updated;
  });
  break;
}
```

### Step 9: Upload Success Feedback — `UploadDropzone.tsx`

After a successful upload, show brief feedback. Simplest approach — a temporary success message:

```typescript
const [successMessage, setSuccessMessage] = useState<string | null>(null);

// After successful upload:
setSuccessMessage(`"${file.name}" uploaded successfully`);
setTimeout(() => setSuccessMessage(null), 3000);

// In render:
{successMessage && (
  <p className="mt-2 text-sm text-green-600">{successMessage}</p>
)}
```

This avoids adding a toast library dependency. If `sonner` is already available via shadcn, use that instead.

### Step 10: README Finalization — `README.md`

Review and add missing sections:

**Add Troubleshooting section:**
```markdown
## Troubleshooting

### "API key not configured" error
Ensure you've set the Anthropic API key via user secrets:
\`\`\`bash
dotnet user-secrets set "Anthropic:ApiKey" "sk-ant-..." --project src/AskMyPdf.Web
\`\`\`

### PDF upload fails with "could not be processed"
Ensure the file is a valid PDF (not a renamed .docx or image). The app validates PDF magic bytes.

### Claude API timeout
Large PDFs (>50 pages) may take longer to process. The first question on a document is slower due to prompt caching initialization. Subsequent questions should be faster.

### Frontend not loading in production
Ensure you've built the frontend and copied files:
\`\`\`bash
cd client && npm run build
# Copy dist/ contents to src/AskMyPdf.Web/wwwroot/
\`\`\`
```

**Add Known Limitations section:**
```markdown
## Known Limitations

- **100-page PDF limit** — Claude API context window constraint
- **32MB file size limit** — Claude API upload constraint
- **Single document per session** — questions are scoped to one document at a time
- **English-optimized** — bounding box matching assumes left-to-right text
- **No conversation memory** — each question is independent (no multi-turn context)
```

**Add Production Build section:**
```markdown
## Production Build

1. Build the frontend:
   \`\`\`bash
   cd client && npm run build
   \`\`\`
2. Copy built files to .NET wwwroot:
   \`\`\`bash
   cp -r client/dist/* src/AskMyPdf.Web/wwwroot/
   \`\`\`
3. Run the .NET app (serves both API + frontend):
   \`\`\`bash
   dotnet run --project src/AskMyPdf.Web -c Release
   \`\`\`
```

### Step 11: Create wwwroot Directory

```bash
mkdir -p src/AskMyPdf.Web/wwwroot
touch src/AskMyPdf.Web/wwwroot/.gitkeep
```

Ensure `.csproj` doesn't exclude `wwwroot/` from content. The default .NET Web SDK includes it automatically.

### Step 12: Final Build Verification

```bash
# Backend
dotnet build AskMyPdf.sln
dotnet test AskMyPdf.sln

# Frontend
cd client && npm run build

# Full production test
cp -r client/dist/* src/AskMyPdf.Web/wwwroot/
dotnet run --project src/AskMyPdf.Web -c Release
# Navigate to http://localhost:5000 — should serve the React app
```

## Patterns to Follow

### Error response pattern (backend)
All error responses use the same shape:
```json
{ "error": "Human-readable error message" }
```

### Frontend component pattern (existing)
```typescript
interface FooProps {
  bar: string;
}

export function Foo({ bar }: FooProps) {
  return <div>{bar}</div>;
}
```

### Loading state pattern
```typescript
const [isLoading, setIsLoading] = useState(true);
// Show skeleton/spinner while loading
// Set false in .then()/.finally()
```

## Risks

| Risk | Mitigation |
|------|------------|
| **wwwroot not picked up by .NET** — static files not served | Default Web SDK includes wwwroot. Verify with `dotnet run` + browser. If not working, add `<Content Include="wwwroot\**" />` to .csproj. |
| **MapFallbackToFile conflicts with API routes** — API routes return HTML | MapFallbackToFile only triggers for non-matched routes. API routes are mapped first and take priority. Verify `/api/documents` still returns JSON. |
| **PdfPig exception on malformed PDF** — unhandled crash | Wrap in try/catch, return BadRequest with clear message. Already planned in Step 3. |
| **Claude API key missing at runtime** — 500 on first question | Add startup validation: check if `Anthropic:ApiKey` is configured, log warning if empty. Don't crash — just return clear error on question endpoint. |
| **sonner/toast dependency bloat** — unnecessary for simple feedback | Use inline success message pattern (Step 9) instead of adding a library. ~5 lines of code vs a dependency. |

## Validation

- [ ] `dotnet build AskMyPdf.sln` — passes with zero warnings relevant to our code
- [ ] `dotnet test AskMyPdf.sln` — all tests pass
- [ ] `cd client && npm run build` — zero TypeScript errors
- [ ] **Static file serving**: copy `client/dist/` → `wwwroot/`, run `dotnet run`, navigate to `http://localhost:5000` → React app loads
- [ ] **API still works**: `GET /api/documents` returns JSON, not HTML
- [ ] **Global error handler**: trigger an error (e.g., corrupt DB file) → get `{ "error": "..." }` JSON, not a stack trace
- [ ] **PDF magic bytes**: upload a renamed `.txt` file as `.pdf` → get clear rejection message
- [ ] **Page count limit**: upload a 150-page PDF → get "exceeds 100-page limit" error
- [ ] **PdfPig crash**: upload a corrupt PDF → get "could not be processed" error, not 500
- [ ] **Claude API error**: disable API key, ask a question → get error message in chat, not a crash
- [ ] **Error boundary**: introduce a rendering error → see "Something went wrong" + "Try Again" button
- [ ] **Document list loading**: refresh page → see skeleton placeholders before documents appear
- [ ] **PDF viewer loading**: select a large PDF → see loading spinner before it renders
- [ ] **Upload success**: upload a PDF → see success message briefly
- [ ] **SSE error event**: when Claude API fails mid-stream → error message appears in chat
- [ ] **README**: follow the README from scratch on a clean machine → app runs successfully
- [ ] **Full demo flow**: upload PDF → ask question → streaming answer → click citation → PDF highlights → everything works end-to-end
