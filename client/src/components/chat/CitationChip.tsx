import type { Citation } from "@/lib/types";
import { BookOpen } from "lucide-react";

interface CitationChipProps {
  citation: Citation;
  onClick: (citation: Citation) => void;
}

export function CitationChip({ citation, onClick }: CitationChipProps) {
  const preview = citation.citedText.length > 80
    ? citation.citedText.slice(0, 80) + "..."
    : citation.citedText;

  const tooltip = citation.citedText.length > 200
    ? citation.citedText.slice(0, 200) + "..."
    : citation.citedText;

  return (
    <button
      onClick={() => onClick(citation)}
      className="flex items-start gap-2 text-left group w-full rounded-lg p-1.5 -mx-1.5 hover:bg-accent/50 transition-colors"
      title={tooltip}
    >
      <span className="inline-flex items-center gap-1 shrink-0 rounded-md bg-primary/10 text-primary px-2 py-0.5 text-xs font-medium group-hover:bg-primary group-hover:text-primary-foreground transition-colors">
        <BookOpen className="h-3 w-3" />
        Page {citation.pageNumber}
      </span>
      <span className="text-xs text-muted-foreground leading-5 line-clamp-2">
        "{preview}"
      </span>
    </button>
  );
}
