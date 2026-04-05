import { useEffect, useState } from "react";
import type { HighlightArea } from "@/lib/types";
import type { RenderHighlightsProps } from "@react-pdf-viewer/highlight";

interface HighlightLayerProps {
  areas: HighlightArea[];
  renderProps: RenderHighlightsProps;
}

export function HighlightLayer({ areas, renderProps }: HighlightLayerProps) {
  const [isPulsing, setIsPulsing] = useState(true);

  useEffect(() => {
    setIsPulsing(true);
    const timer = setTimeout(() => setIsPulsing(false), 600);
    return () => clearTimeout(timer);
  }, [areas]);

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
            transition: "opacity 0.6s ease-out, box-shadow 0.6s ease-out",
            opacity: isPulsing ? 0.7 : 1,
            boxShadow: isPulsing ? "0 0 8px 2px rgba(255, 235, 59, 0.4)" : "none",
          }}
        />
      ))}
    </div>
  );
}
