import { useEffect, useMemo } from "react";
import { Viewer, Worker, SpecialZoomLevel } from "@react-pdf-viewer/core";
import { highlightPlugin, Trigger } from "@react-pdf-viewer/highlight";
import type { RenderHighlightsProps } from "@react-pdf-viewer/highlight";
import "@react-pdf-viewer/core/lib/styles/index.css";
import "@react-pdf-viewer/highlight/lib/styles/index.css";
import { getDocumentFileUrl } from "@/lib/api";
import { WORKER_URL } from "@/lib/pdfWorker";
import { HighlightLayer } from "./HighlightLayer";
import type { Citation } from "@/lib/types";

interface PdfViewerPanelProps {
  documentId: string;
  activeCitation: Citation | null;
}

export function PdfViewerPanel({ documentId, activeCitation }: PdfViewerPanelProps) {
  const fileUrl = getDocumentFileUrl(documentId);

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
    <div className="h-full w-full">
      <Worker workerUrl={WORKER_URL}>
        <Viewer
          fileUrl={fileUrl}
          plugins={plugins}
          defaultScale={SpecialZoomLevel.PageWidth}
        />
      </Worker>
    </div>
  );
}
