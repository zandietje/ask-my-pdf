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

function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

export function DocumentList({ documents, selectedId, onSelect, onDelete, isLoading }: DocumentListProps) {
  if (isLoading) {
    return (
      <div className="space-y-1">
        {[1, 2, 3].map(i => (
          <Skeleton key={i} className="h-12 md:h-10 w-full rounded-lg" />
        ))}
      </div>
    );
  }

  if (documents.length === 0) {
    return (
      <div className="flex flex-col items-center gap-1.5 py-4 text-center">
        <FileText className="h-5 w-5 text-muted-foreground/40" />
        <p className="text-sm md:text-xs text-muted-foreground">No documents yet</p>
      </div>
    );
  }

  return (
    <div className="space-y-1 md:space-y-0.5">
      {documents.map(doc => {
        const isSelected = doc.id === selectedId;
        return (
          <div
            key={doc.id}
            className={cn(
              "flex w-full items-center gap-2 rounded-lg px-3 py-3 md:px-2.5 md:py-2 text-sm transition-all group",
              isSelected
                ? "bg-primary/8 border-l-2 border-primary pl-2.5 md:pl-2"
                : "hover:bg-accent/50 border-l-2 border-transparent"
            )}
          >
            <button
              onClick={() => onSelect(doc)}
              className="flex items-center gap-2.5 md:gap-2 min-w-0 flex-1 text-left"
            >
              <FileText className={cn(
                "h-5 w-5 md:h-4 md:w-4 shrink-0",
                isSelected ? "text-primary" : "text-muted-foreground"
              )} />
              <div className="min-w-0 flex-1">
                <span className={cn(
                  "block truncate text-sm md:text-[13px]",
                  isSelected && "font-medium text-primary"
                )}>{doc.fileName}</span>
                <span className="block text-xs md:text-[11px] text-muted-foreground tabular-nums">
                  {doc.pageCount} {doc.pageCount === 1 ? "page" : "pages"}{doc.fileSize ? ` · ${formatFileSize(doc.fileSize)}` : ""}
                </span>
              </div>
            </button>
            <button
              onClick={(e) => {
                e.stopPropagation();
                onDelete(doc);
              }}
              className="shrink-0 p-2 md:p-1 rounded-md text-muted-foreground/50 opacity-100 md:opacity-0 md:group-hover:opacity-100 hover:bg-destructive/10 hover:text-destructive transition-all"
              title="Delete document"
            >
              <Trash2 className="h-4 w-4 md:h-3.5 md:w-3.5" />
            </button>
          </div>
        );
      })}
    </div>
  );
}
