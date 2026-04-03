import { useEffect, useRef, useState } from "react";
import { Viewer, Worker, SpecialZoomLevel } from "@react-pdf-viewer/core";
import { highlightPlugin, Trigger } from "@react-pdf-viewer/highlight";
import type { RenderHighlightsProps } from "@react-pdf-viewer/highlight";
import "@react-pdf-viewer/core/lib/styles/index.css";
import "@react-pdf-viewer/highlight/lib/styles/index.css";
import { getDocumentFileUrl } from "@/lib/api";
import { WORKER_URL } from "@/lib/pdfWorker";
import { HighlightLayer } from "./HighlightLayer";
import type { Citation } from "@/lib/types";
import { Loader2 } from "lucide-react";

interface PdfViewerPanelProps {
  documentId: string;
  activeCitation: Citation | null;
}

export function PdfViewerPanel({ documentId, activeCitation }: PdfViewerPanelProps) {
  const fileUrl = getDocumentFileUrl(documentId);
  const [isLoading, setIsLoading] = useState(true);

  // Reset loading state when document changes
  useEffect(() => {
    setIsLoading(true);
  }, [documentId]);

  // Use ref so renderHighlights always reads the latest citation
  // without needing to re-mount the Viewer
  const citationRef = useRef<Citation | null>(null);
  citationRef.current = activeCitation;

  // highlightPlugin uses hooks internally — must be called at top level, not in useMemo
  const highlightPluginInstance = highlightPlugin({
    renderHighlights: (props: RenderHighlightsProps) => {
      const citation = citationRef.current;
      if (!citation) return <></>;
      const pageAreas = citation.highlightAreas.filter(
        (a) => a.pageIndex === props.pageIndex
      );
      if (pageAreas.length === 0) return <></>;
      return <HighlightLayer areas={pageAreas} renderProps={props} />;
    },
    trigger: Trigger.None,
  });

  const { jumpToHighlightArea } = highlightPluginInstance;

  // Keep a stable ref to jumpToHighlightArea so the effect below
  // only re-runs when activeCitation changes, not on every render
  const jumpRef = useRef(jumpToHighlightArea);
  jumpRef.current = jumpToHighlightArea;

  const containerRef = useRef<HTMLDivElement>(null);

  // Jump to the first highlight area when citation changes — no re-mount needed
  useEffect(() => {
    if (activeCitation && activeCitation.highlightAreas.length > 0) {
      const timer = setTimeout(() => {
        jumpRef.current(activeCitation.highlightAreas[0]);

        // After jump puts highlight at top, scroll back to center it
        requestAnimationFrame(() => {
          const scrollEl = containerRef.current?.querySelector('[data-testid="core__inner-pages"]')
            ?? containerRef.current?.querySelector('.rpv-core__inner-pages');
          if (scrollEl) {
            const viewerHeight = scrollEl.clientHeight;
            scrollEl.scrollTop = Math.max(0, scrollEl.scrollTop - viewerHeight / 3);
          }
        });
      }, 50);
      return () => clearTimeout(timer);
    }
  }, [activeCitation]);

  return (
    <div ref={containerRef} className="relative h-full w-full overflow-hidden">
      {isLoading && (
        <div className="absolute inset-0 z-10 flex items-center justify-center bg-background/80">
          <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
        </div>
      )}
      <Worker workerUrl={WORKER_URL}>
        <Viewer
          key={documentId}
          fileUrl={fileUrl}
          plugins={[highlightPluginInstance]}
          defaultScale={SpecialZoomLevel.PageWidth}
          onDocumentLoad={() => setIsLoading(false)}
        />
      </Worker>
    </div>
  );
}
