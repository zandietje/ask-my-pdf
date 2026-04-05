import type { Citation } from "@/lib/types";
import { FileText } from "lucide-react";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui/tooltip";

interface CitationChipProps {
  citation: Citation;
  onClick: (citation: Citation) => void;
}

export function CitationChip({ citation, onClick }: CitationChipProps) {
  const shortText =
    citation.citedText.length > 80
      ? citation.citedText.slice(0, 80) + "..."
      : citation.citedText;

  return (
    <TooltipProvider delayDuration={300}>
      <Tooltip>
        <TooltipTrigger asChild>
          <button
            onClick={() => onClick(citation)}
            className="inline-flex items-center gap-1.5 rounded-lg bg-citation-bg border border-citation-border
                       hover:border-citation text-citation-text px-2.5 py-1.5 text-xs transition-colors
                       focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-1"
          >
            <FileText className="h-3 w-3 shrink-0" />
            <span className="font-semibold">p. {citation.pageNumber}</span>
            <span className="text-citation-text/70 truncate max-w-[200px] hidden sm:inline">
              {shortText}
            </span>
          </button>
        </TooltipTrigger>
        <TooltipContent side="top" className="max-w-xs">
          <p className="text-xs italic leading-relaxed">
            "{citation.citedText.length > 300
              ? citation.citedText.slice(0, 300) + "..."
              : citation.citedText}"
          </p>
          <p className="text-xs text-muted-foreground mt-1">
            Click to view in document
          </p>
        </TooltipContent>
      </Tooltip>
    </TooltipProvider>
  );
}
