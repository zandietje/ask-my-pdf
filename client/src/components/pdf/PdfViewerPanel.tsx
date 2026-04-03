import { useEffect, useMemo, useState } from "react";
import { Viewer, Worker, SpecialZoomLevel } from "@react-pdf-viewer/core";
import { highlightPlugin, Trigger } from "@react-pdf-viewer/highlight";
import type { RenderHighlightsProps } from "@react-pdf-viewer/highlight";
import "@react-pdf-viewer/core/lib/styles/index.css";
import "@react-pdf-viewer/highlight/lib/styles/index.css";
import { getDocumentFileUrl } from "@/lib/api";
import { WORKER_URL } from "@/lib/pdfWorker";
import { HighlightLayer } from "./HighlightLayer";
import { Loader2, AlertTriangle } from "lucide-react";
import type { Citation } from "@/lib/types";

interface PdfViewerPanelProps {
  documentId: string;
  activeCitation: Citation | null;
}

export function PdfViewerPanel({ documentId, activeCitation }: PdfViewerPanelProps) {
  const fileUrl = getDocumentFileUrl(documentId);
  const [pdfLoading, setPdfLoading] = useState(true);
  const [loadError, setLoadError] = useState(false);

  // Reset loading state when document changes
  useEffect(() => {
    setPdfLoading(true);
    setLoadError(false);
  }, [documentId]);

  // Recreate plugin when citation changes so renderHighlights captures the new value
  const highlightPluginInstance = useMemo(
    () =>
      highlightPlugin({
        renderHighlights: (props: RenderHighlightsProps) => {
          if (!activeCitation) return <></>;
          const pageAreas = activeCitation.highlightAreas.filter(
            (a) => a.pageIndex === props.pageIndex
          );
          if (pageAreas.length === 0) return <></>;
          return <HighlightLayer areas={pageAreas} renderProps={props} />;
        },
        trigger: Trigger.None,
      }),
    [activeCitation]
  );

  const { jumpToHighlightArea } = highlightPluginInstance;

  const plugins = useMemo(
    () => [highlightPluginInstance],
    [highlightPluginInstance]
  );

  // Jump to the first highlight area when citation changes
  useEffect(() => {
    if (activeCitation && activeCitation.highlightAreas.length > 0) {
      jumpToHighlightArea(activeCitation.highlightAreas[0]);
    }
  }, [activeCitation, jumpToHighlightArea]);

  return (
    <div className="relative h-full w-full">
      <Worker workerUrl={WORKER_URL}>
        <Viewer
          fileUrl={fileUrl}
          plugins={plugins}
          defaultScale={SpecialZoomLevel.PageWidth}
          onDocumentLoad={() => setPdfLoading(false)}
          renderError={() => {
            setLoadError(true);
            return (
              <div className="flex h-full items-center justify-center">
                <div className="text-center space-y-2">
                  <AlertTriangle className="mx-auto h-8 w-8 text-destructive" />
                  <p className="text-sm text-destructive">
                    Failed to load PDF. The file may be corrupted.
                  </p>
                </div>
              </div>
            );
          }}
        />
      </Worker>
      {pdfLoading && !loadError && (
        <div className="absolute inset-0 flex items-center justify-center bg-background/80">
          <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
        </div>
      )}
    </div>
  );
}
