import type { Citation } from "@/lib/types";
import { BookOpen, ArrowRight } from "lucide-react";

interface CitationChipProps {
  citation: Citation;
  onClick: (citation: Citation) => void;
}

export function CitationChip({ citation, onClick }: CitationChipProps) {
  const preview = citation.citedText.length > 120
    ? citation.citedText.slice(0, 120) + "..."
    : citation.citedText;

  const tooltip = citation.citedText.length > 200
    ? citation.citedText.slice(0, 200) + "..."
    : citation.citedText;

  return (
    <button
      onClick={() => onClick(citation)}
      className="flex items-start gap-2.5 text-left group w-full rounded-lg bg-citation-bg border border-citation-border hover:border-citation hover:bg-citation-bg p-3 md:p-2.5 transition-colors"
      title={tooltip}
    >
      <span className="inline-flex items-center gap-1 shrink-0 rounded-md bg-citation/10 text-citation-text px-2.5 py-1 md:px-2 md:py-0.5 text-sm md:text-xs font-semibold">
        <BookOpen className="h-3.5 w-3.5 md:h-3 md:w-3" />
        p. {citation.pageNumber}
      </span>
      <span className="flex-1 min-w-0">
        <span className="text-sm md:text-[13px] text-foreground/80 leading-relaxed line-clamp-2 italic">
          "{preview}"
        </span>
      </span>
      <ArrowRight className="h-4 w-4 md:h-3.5 md:w-3.5 shrink-0 mt-0.5 text-citation opacity-100 md:opacity-0 md:group-hover:opacity-100 transition-opacity" />
    </button>
  );
}
