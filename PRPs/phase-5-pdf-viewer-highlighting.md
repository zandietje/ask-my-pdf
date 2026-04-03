# PRP: Phase 5 — PDF Viewer + Citation Highlighting

## Objective
Add the PDF viewer panel with programmatic highlighting — clicking a citation chip in the chat scrolls to the correct page and highlights the exact cited passage with a yellow overlay. This is the visual differentiator (assessment criterion #4).

## Scope
**In scope:**
- `PdfViewerPanel` — renders the selected PDF using `@react-pdf-viewer/core` + highlight plugin
- `HighlightLayer` — renders yellow overlay rectangles from highlight area coordinates
- PDF worker configuration for `pdfjs-dist`
- Wire citation click → set active citation → PDF viewer jumps to page + highlights passage
- Handle multiple highlight areas per citation (multi-line passages)
- Handle switching between citations on different pages
- Handle loading a new document when switching selected doc

**Out of scope:**
- Error handling polish, edge cases (Phase 6)
- Production static file serving (Phase 6)
- Multi-document citation switching (asking about one doc while viewing another)
- Search/text-selection features in the PDF viewer

## Prerequisites
- Phase 1-4 complete: full chat UI working with streaming answers + citation chips
- `@react-pdf-viewer/core` 3.12.0, `@react-pdf-viewer/highlight` 3.12.0, `pdfjs-dist` 3.11.174 already installed
- Backend endpoints working:
  - `GET /api/documents/{id}/file` — returns PDF binary
  - `POST /api/questions` — SSE stream with `citation` events containing `highlightAreas[]`
- `HighlightArea` type already defined in `client/src/lib/types.ts`:
  ```typescript
  { pageIndex: number, left: number, top: number, width: number, height: number }
  ```
  - `pageIndex` is 0-based (page 1 = index 0)
  - `left`, `top`, `width`, `height` are percentages 0-100
- `Citation` type already defined with `highlightAreas: HighlightArea[]`
- `activeCitation` state already managed in `App.tsx`, set by `handleCitationClick`
- `getDocumentFileUrl(documentId)` already in `api.ts` returning `/api/documents/{id}/file`

## Outputs
When done:
- Right panel renders the selected PDF document
- Clicking a citation chip in chat → PDF scrolls to the cited page and highlights the passage in yellow
- Yellow overlay rectangles appear at the exact coordinates from the backend's bounding box data
- Switching between citations on different pages works correctly
- Switching documents loads the new PDF
- `npm run build` passes with zero TypeScript errors

## Files to Create/Modify

### New Files
| File | Action | Description |
|------|--------|-------------|
| `client/src/lib/pdfWorker.ts` | Create | Configure pdfjs-dist worker URL |
| `client/src/components/pdf/PdfViewerPanel.tsx` | Create | PDF viewer with @react-pdf-viewer/core + highlight plugin |
| `client/src/components/pdf/HighlightLayer.tsx` | Create | Renders yellow highlight overlay rectangles |

### Modified Files
| File | Action | Description |
|------|--------|-------------|
| `client/src/App.tsx` | Modify | Replace right panel placeholder with PdfViewerPanel, pass activeCitation + selectedDoc |
| `client/src/main.tsx` | Modify | Import pdfWorker setup |

## NPM Packages Needed
All already in `package.json` — no new packages.

| Package | Version | Status |
|---------|---------|--------|
| `@react-pdf-viewer/core` | ^3.12.0 | Already installed |
| `@react-pdf-viewer/highlight` | ^3.12.0 | Already installed |
| `pdfjs-dist` | ^3.11.174 | Already installed |

## Implementation Steps

### Step 1: PDF Worker Configuration — `client/src/lib/pdfWorker.ts`

`pdfjs-dist` requires a web worker for PDF rendering. Configure it to use the correct version.

```typescript
import { GlobalWorkerOptions } from "pdfjs-dist";

// Use the worker from the installed pdfjs-dist package
// The worker file is at node_modules/pdfjs-dist/build/pdf.worker.min.js
// Vite can serve this via the ?url import or we can set the CDN URL matching our exact version
GlobalWorkerOptions.workerSrc = new URL(
  "pdfjs-dist/build/pdf.worker.min.js",
  import.meta.url,
).toString();
```

**Alternative if the import.meta.url approach fails:** Use the unpkg CDN URL matching the exact installed version:
```typescript
GlobalWorkerOptions.workerSrc = "https://unpkg.com/pdfjs-dist@3.11.174/build/pdf.worker.min.js";
```

Import this file early in `client/src/main.tsx` before React renders:
```typescript
import "./lib/pdfWorker";
```

### Step 2: HighlightLayer — `client/src/components/pdf/HighlightLayer.tsx`

This component renders yellow overlay rectangles on top of the PDF page.

The `@react-pdf-viewer/highlight` plugin provides a `renderHighlights` callback that receives the current page index and a method to render highlights. However, the simpler approach is to use the plugin's built-in highlight area rendering.

**Key `@react-pdf-viewer/highlight` API:**

The highlight plugin's `renderHighlightTarget` callback receives:
- `highlightAreas: HighlightArea[]` — the areas to highlight (percentage-based)
- `toggle()` — to toggle the highlight state

But the most straightforward approach is using the plugin's `highlightPluginInstance.jumpToHighlightArea(area)` for navigation and `renderHighlights` for rendering overlays.

```typescript
import type { HighlightArea } from "@/lib/types";

interface HighlightLayerProps {
  areas: HighlightArea[];
}

export function HighlightLayer({ areas }: HighlightLayerProps) {
  // Renders absolutely-positioned yellow rectangles
  // Each HighlightArea has: pageIndex, left, top, width, height (all percentages 0-100)
  // These are rendered as overlays on the PDF page
  return (
    <>
      {areas.map((area, i) => (
        <div
          key={i}
          style={{
            position: "absolute",
            left: `${area.left}%`,
            top: `${area.top}%`,
            width: `${area.width}%`,
            height: `${area.height}%`,
            backgroundColor: "rgba(255, 235, 59, 0.35)",
            mixBlendMode: "multiply",
            pointerEvents: "none",
          }}
        />
      ))}
    </>
  );
}
```

### Step 3: PdfViewerPanel — `client/src/components/pdf/PdfViewerPanel.tsx`

This is the main PDF viewer component. It uses `@react-pdf-viewer/core`'s `Viewer` component with the highlight plugin.

**Key @react-pdf-viewer APIs:**

```typescript
import { Viewer, Worker } from "@react-pdf-viewer/core";
import { highlightPlugin, RenderHighlightsProps } from "@react-pdf-viewer/highlight";
import "@react-pdf-viewer/core/lib/styles/index.css";
import "@react-pdf-viewer/highlight/lib/styles/index.css";
```

The highlight plugin provides:
- `highlightPluginInstance.jumpToHighlightArea(area)` — scrolls the viewer to a specific highlight area
- `renderHighlights` — callback to render custom highlight overlays on each page

**Component structure:**

```typescript
import { useEffect, useRef } from "react";
import { Viewer } from "@react-pdf-viewer/core";
import { highlightPlugin, RenderHighlightsProps } from "@react-pdf-viewer/highlight";
import "@react-pdf-viewer/core/lib/styles/index.css";
import "@react-pdf-viewer/highlight/lib/styles/index.css";
import { getDocumentFileUrl } from "@/lib/api";
import type { Citation } from "@/lib/types";

interface PdfViewerPanelProps {
  documentId: string;
  activeCitation: Citation | null;
}

export function PdfViewerPanel({ documentId, activeCitation }: PdfViewerPanelProps) {
  const fileUrl = getDocumentFileUrl(documentId);

  // Create highlight plugin instance
  const highlightPluginInstance = highlightPlugin({
    renderHighlights: (props: RenderHighlightsProps) => {
      // Filter highlight areas for the current page
      if (!activeCitation) return <></>;
      const pageAreas = activeCitation.highlightAreas.filter(
        a => a.pageIndex === props.pageIndex
      );
      if (pageAreas.length === 0) return <></>;

      return (
        <div>
          {pageAreas.map((area, i) => (
            <div
              key={i}
              style={Object.assign(
                {},
                // props.getCssProperties(area) converts percentage-based area to absolute CSS
                // @react-pdf-viewer/highlight expects: { left, top, width, height } as percentages
                props.getCssProperties(
                  {
                    // @react-pdf-viewer expects pageIndex, left, top, width, height
                    pageIndex: area.pageIndex,
                    left: area.left,
                    top: area.top,
                    width: area.width,
                    height: area.height,
                  },
                  props.rotation
                ),
                {
                  backgroundColor: "rgba(255, 235, 59, 0.35)",
                  mixBlendMode: "multiply" as const,
                  pointerEvents: "none" as const,
                },
              )}
            />
          ))}
        </div>
      );
    },
  });

  const { jumpToHighlightArea } = highlightPluginInstance;

  // Jump to highlight area when activeCitation changes
  useEffect(() => {
    if (activeCitation && activeCitation.highlightAreas.length > 0) {
      const firstArea = activeCitation.highlightAreas[0];
      jumpToHighlightArea({
        pageIndex: firstArea.pageIndex,
        left: firstArea.left,
        top: firstArea.top,
        width: firstArea.width,
        height: firstArea.height,
      });
    }
  }, [activeCitation, jumpToHighlightArea]);

  return (
    <div className="h-full w-full">
      <Viewer
        fileUrl={fileUrl}
        plugins={[highlightPluginInstance]}
      />
    </div>
  );
}
```

**Critical implementation details:**

1. **`getCssProperties` method**: The `renderHighlights` callback in `@react-pdf-viewer/highlight` provides `getCssProperties(area, rotation)` which converts percentage-based coordinates to CSS properties positioned correctly within the rendered page. This handles zoom, rotation, and scroll position automatically.

2. **`jumpToHighlightArea`**: Takes a `HighlightArea` object and scrolls the viewer to make that area visible. The area uses the same percentage-based coordinate system as our backend output.

3. **`@react-pdf-viewer/highlight` HighlightArea format**: The plugin expects `{ pageIndex, left, top, width, height }` — this matches our backend `HighlightArea` model exactly (both use 0-based page index and 0-100 percentages).

4. **Re-renders**: When `activeCitation` changes, the `renderHighlights` callback re-renders because it reads from `activeCitation` in the closure. The `useEffect` triggers `jumpToHighlightArea` to scroll to the new position.

### Step 4: Update App.tsx — Replace placeholder with PdfViewerPanel

Replace the right panel placeholder div with the actual `PdfViewerPanel` component:

```typescript
import { PdfViewerPanel } from "@/components/pdf/PdfViewerPanel";

// In the App component, replace the rightPanel variable:
const rightPanel = selectedDoc ? (
  <PdfViewerPanel
    documentId={selectedDoc.id}
    activeCitation={activeCitation}
  />
) : (
  <div className="flex h-full items-center justify-center bg-muted/30 text-muted-foreground">
    <p>Upload and select a document to view it here</p>
  </div>
);
```

### Step 5: Update main.tsx — Import PDF worker setup

Add the pdfWorker import at the top of `client/src/main.tsx` (before React imports):

```typescript
import "./lib/pdfWorker";
import { StrictMode } from "react";
// ... rest of imports
```

### Step 6: Verify and fix @react-pdf-viewer types

The `@react-pdf-viewer/highlight` plugin's `RenderHighlightsProps` type includes:
- `pageIndex: number` — the current page being rendered (0-based)
- `rotation: number` — current rotation
- `getCssProperties(area, rotation)` — converts area coordinates to CSS

The `HighlightArea` type expected by the plugin:
```typescript
interface HighlightArea {
  pageIndex: number;
  left: number;
  top: number;
  width: number;
  height: number;
}
```

This matches our `HighlightArea` type in `types.ts` exactly. No adapter needed.

**Important**: If `@react-pdf-viewer/highlight` v3.12.0 has different type signatures, check:
```typescript
import type { HighlightArea as PdfHighlightArea } from "@react-pdf-viewer/highlight";
```
and map if needed.

### Step 7: Handle the @react-pdf-viewer/highlight renderHighlights stale closure

The `renderHighlights` callback is passed to `highlightPlugin()` at creation time. If it captures `activeCitation` from the closure, it will go stale when `activeCitation` changes.

**Solution**: Use a `useRef` to hold the current `activeCitation` and read from the ref inside `renderHighlights`:

```typescript
const activeCitationRef = useRef(activeCitation);
activeCitationRef.current = activeCitation;

const highlightPluginInstance = useMemo(() =>
  highlightPlugin({
    renderHighlights: (props: RenderHighlightsProps) => {
      const citation = activeCitationRef.current;
      if (!citation) return <></>;
      // ... render highlights using citation
    },
  }),
[]); // Empty deps — plugin created once, reads from ref
```

This ensures the plugin instance is stable (created once) but always reads the latest `activeCitation` via the ref.

**Trigger re-render**: The highlight plugin re-renders overlays when the page changes or the viewer re-renders. To force a re-render when `activeCitation` changes, use a key on the parent or call `forceUpdate`. The simplest approach: add a `key` prop to the Viewer based on the citation:

```typescript
<Viewer
  key={activeCitation ? `${activeCitation.pageNumber}-${activeCitation.citedText.slice(0, 20)}` : "default"}
  fileUrl={fileUrl}
  plugins={[highlightPluginInstance]}
/>
```

**Caution**: Changing the Viewer key reloads the PDF. A better approach is to trigger re-render of only the highlight layer. The plugin's `switchTrigger` or a state counter can help:

```typescript
const [highlightKey, setHighlightKey] = useState(0);

useEffect(() => {
  setHighlightKey(k => k + 1);
}, [activeCitation]);
```

Then in `renderHighlights`, read from the ref. The state change triggers a re-render of PdfViewerPanel, which causes Viewer to re-render its children (including the highlight layer).

### Fallback Strategy

If `@react-pdf-viewer/highlight` plugin has issues (the package was archived March 2026):

**Fallback A — Manual absolute positioning:**
- Don't use the highlight plugin at all
- Use just `@react-pdf-viewer/core` `Viewer`
- Overlay highlights using CSS absolute positioning on a container that wraps each page
- Use `onPageChange` to track current page, manually scroll with `scrollTo`

**Fallback B — Switch to `react-pdf` (wojtekmaj):**
- Actively maintained, works with React 18
- `<Document>` + `<Page>` components
- Use `customTextRenderer` for text-based highlighting
- Or overlay with absolute-positioned divs (same coordinate system)
- Does NOT have `jumpToHighlightArea` — implement manual scroll

Try the primary approach first (`@react-pdf-viewer/highlight`). Only switch to fallback if it fails at runtime.

## Patterns to Follow

### Component pattern (matching existing code)
```typescript
interface FooProps {
  bar: string;
  onBaz: () => void;
}

export function Foo({ bar, onBaz }: FooProps) {
  return <div>...</div>;
}
```

### Import pattern
```typescript
import { Viewer } from "@react-pdf-viewer/core";
import { highlightPlugin } from "@react-pdf-viewer/highlight";
import "@react-pdf-viewer/core/lib/styles/index.css";
import "@react-pdf-viewer/highlight/lib/styles/index.css";
import { getDocumentFileUrl } from "@/lib/api";
import type { Citation } from "@/lib/types";
```

### Coordinate system alignment
Backend `HighlightArea` output → Frontend `@react-pdf-viewer/highlight` input:
```
pageIndex: 0-based ✅ (both use 0-based)
left:      0-100 %  ✅ (both use percentages)
top:       0-100 %  ✅ (both use percentages, both origin top-left)
width:     0-100 %  ✅ (both use percentages)
height:    0-100 %  ✅ (both use percentages)
```
No coordinate transformation needed on the frontend — the backend CoordinateTransformer already converts from PdfPig's bottom-left origin to the viewer's top-left origin.

## Risks

| Risk | Mitigation |
|------|------------|
| **@react-pdf-viewer archived (March 2026)** — may have bugs or fail to render | The package is archived but functional. It works with React 18 + pdfjs-dist 3.x. If it fails, switch to `react-pdf` (wojtekmaj) with manual highlight overlays (Fallback B). |
| **pdfjs-dist worker not loading** — PDF won't render without worker | Try `import.meta.url` approach first. Fallback to unpkg CDN URL. Verify in browser console for worker-related errors. |
| **Stale closure in renderHighlights** — highlights don't update when citation changes | Use `useRef` for `activeCitation` inside the render callback. Force re-render via state counter. |
| **Highlight coordinates misaligned** — yellow overlay doesn't match text | Backend CoordinateTransformer already tested. If alignment is off, check if @react-pdf-viewer expects different percentage scale or has a different coordinate origin. Debug with a known PDF + known coordinates. |
| **getCssProperties API different than expected** — the highlight plugin's type API may have changed | Check actual types from `node_modules/@react-pdf-viewer/highlight/lib/`. If `getCssProperties` doesn't exist, compute CSS manually: `left: ${area.left}%`, `top: ${area.top}%`, etc. in an absolutely-positioned container. |
| **Viewer re-renders on every activeCitation change** — performance issue | Plugin instance is stable (useMemo). Only the highlight layer re-renders, not the full PDF. If still slow, debounce citation clicks. |

## Validation

- [ ] `npm run build` — zero TypeScript errors
- [ ] `npm run dev` — app renders with PDF viewer in right panel
- [ ] Upload a PDF → right panel shows the rendered PDF (all pages visible, scrollable)
- [ ] Ask a question → get answer with citations → click a citation chip → PDF scrolls to the correct page
- [ ] Yellow highlight overlay appears at the correct position over the cited text
- [ ] Click a different citation on a different page → PDF scrolls to that page with new highlight
- [ ] Multi-line citations: highlight rectangles span multiple lines correctly
- [ ] Switch to a different document → PDF viewer loads the new document
- [ ] No document selected → right panel shows "Upload and select a document" placeholder
- [ ] Browser console has no errors related to pdfjs-dist worker
- [ ] PDF viewer handles zoom (scroll zoom or pinch) — highlights stay aligned
