import type { Citation } from "@/lib/types";
import { Badge } from "@/components/ui/badge";

interface CitationChipProps {
  citation: Citation;
  onClick: (citation: Citation) => void;
}

export function CitationChip({ citation, onClick }: CitationChipProps) {
  const preview = citation.citedText.length > 80
    ? citation.citedText.slice(0, 80) + "..."
    : citation.citedText;

  return (
    <button
      onClick={() => onClick(citation)}
      className="flex items-start gap-1.5 text-left group"
      title={citation.citedText}
    >
      <Badge
        variant="secondary"
        className="shrink-0 cursor-pointer group-hover:bg-primary group-hover:text-primary-foreground transition-colors"
      >
        Page {citation.pageNumber}
      </Badge>
      <span className="text-xs text-muted-foreground leading-5 line-clamp-2">
        "{preview}"
      </span>
    </button>
  );
}
