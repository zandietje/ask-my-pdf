import type { DocumentDto } from "@/lib/types";
import { FileText, Trash2 } from "lucide-react";
import { Skeleton } from "@/components/ui/skeleton";
import { cn } from "@/lib/utils";

interface DocumentListProps {
  documents: DocumentDto[];
  selectedId: string | null;
  onSelect: (doc: DocumentDto) => void;
  onDelete: (doc: DocumentDto) => void;
  isLoading?: boolean;
}

export function DocumentList({ documents, selectedId, onSelect, onDelete, isLoading }: DocumentListProps) {
  if (isLoading) {
    return (
      <div className="space-y-1.5">
        {[1, 2, 3].map(i => (
          <Skeleton key={i} className="h-9 w-full rounded-lg" />
        ))}
      </div>
    );
  }

  if (documents.length === 0) {
    return (
      <p className="text-xs text-muted-foreground text-center py-2">No documents uploaded yet</p>
    );
  }

  return (
    <div className="space-y-0.5">
      {documents.map(doc => (
        <div
          key={doc.id}
          className={cn(
            "flex w-full items-center gap-2 rounded-lg px-2.5 py-2 text-sm transition-all group",
            doc.id === selectedId
              ? "bg-primary/10 text-primary font-medium"
              : "hover:bg-accent/50"
          )}
        >
          <button
            onClick={() => onSelect(doc)}
            className="flex items-center gap-2 min-w-0 flex-1 text-left"
          >
            <FileText className={cn(
              "h-4 w-4 shrink-0",
              doc.id === selectedId ? "text-primary" : "text-muted-foreground"
            )} />
            <span className="truncate text-[13px]">{doc.fileName}</span>
          </button>
          <span className="shrink-0 text-[11px] text-muted-foreground tabular-nums">
            {doc.pageCount}p
          </span>
          <button
            onClick={(e) => {
              e.stopPropagation();
              onDelete(doc);
            }}
            className="shrink-0 p-1 rounded-md opacity-0 group-hover:opacity-100 hover:bg-destructive/10 hover:text-destructive transition-all"
            title="Delete document"
          >
            <Trash2 className="h-3.5 w-3.5" />
          </button>
        </div>
      ))}
    </div>
  );
}
