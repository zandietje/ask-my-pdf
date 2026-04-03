import type { DocumentDto } from "@/lib/types";
import { FileText } from "lucide-react";
import { Skeleton } from "@/components/ui/skeleton";
import { cn } from "@/lib/utils";

interface DocumentListProps {
  documents: DocumentDto[];
  selectedId: string | null;
  onSelect: (doc: DocumentDto) => void;
  isLoading?: boolean;
}

export function DocumentList({ documents, selectedId, onSelect, isLoading }: DocumentListProps) {
  if (isLoading) {
    return (
      <div className="space-y-1.5">
        {[1, 2, 3].map(i => (
          <Skeleton key={i} className="h-8 w-full" />
        ))}
      </div>
    );
  }

  if (documents.length === 0) {
    return (
      <p className="text-sm text-muted-foreground">No documents uploaded yet</p>
    );
  }

  return (
    <div className="space-y-1">
      {documents.map(doc => (
        <button
          key={doc.id}
          onClick={() => onSelect(doc)}
          className={cn(
            "flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-left text-sm transition-colors",
            doc.id === selectedId
              ? "bg-accent text-accent-foreground"
              : "hover:bg-muted"
          )}
        >
          <FileText className="h-4 w-4 shrink-0 text-muted-foreground" />
          <span className="truncate">{doc.fileName}</span>
          <span className="ml-auto shrink-0 text-xs text-muted-foreground">
            {doc.pageCount}p
          </span>
        </button>
      ))}
    </div>
  );
}
