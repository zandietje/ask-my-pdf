import type { HighlightArea } from "@/lib/types";
import type { RenderHighlightsProps } from "@react-pdf-viewer/highlight";

interface HighlightLayerProps {
  areas: HighlightArea[];
  renderProps: RenderHighlightsProps;
}

export function HighlightLayer({ areas, renderProps }: HighlightLayerProps) {
  return (
    <div>
      {areas.map((area, i) => (
        <div
          key={i}
          style={{
            ...renderProps.getCssProperties(area, renderProps.rotation),
            backgroundColor: "rgba(255, 235, 59, 0.35)",
            mixBlendMode: "multiply" as const,
            pointerEvents: "none" as const,
          }}
        />
      ))}
    </div>
  );
}
