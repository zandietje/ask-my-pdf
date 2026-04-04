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
      className="flex items-start gap-2.5 text-left group w-full rounded-lg bg-amber-50/70 border border-amber-200/60 hover:border-amber-300 hover:bg-amber-50 p-2.5 transition-colors"
      title={tooltip}
    >
      <span className="inline-flex items-center gap-1 shrink-0 rounded-md bg-amber-100 text-amber-800 px-2 py-0.5 text-xs font-semibold">
        <BookOpen className="h-3 w-3" />
        p. {citation.pageNumber}
      </span>
      <span className="flex-1 min-w-0">
        <span className="text-[13px] text-foreground/80 leading-relaxed line-clamp-2 italic">
          "{preview}"
        </span>
      </span>
      <ArrowRight className="h-3.5 w-3.5 shrink-0 mt-0.5 text-amber-400 opacity-0 group-hover:opacity-100 transition-opacity" />
    </button>
  );
}
