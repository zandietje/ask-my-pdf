import type { DocumentDto } from "@/lib/types";
import { FileText, Trash2 } from "lucide-react";
import { cn } from "@/lib/utils";

interface DocumentItemProps {
  doc: DocumentDto;
  isSelected: boolean;
  onSelect: (doc: DocumentDto) => void;
  onDelete: (doc: DocumentDto) => void;
}

function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

function formatRelativeTime(dateStr: string): string {
  const now = new Date();
  const date = new Date(dateStr);
  const diffMs = now.getTime() - date.getTime();
  const diffMin = Math.floor(diffMs / 60000);
  if (diffMin < 1) return "just now";
  if (diffMin < 60) return `${diffMin}m ago`;
  const diffHr = Math.floor(diffMin / 60);
  if (diffHr < 24) return `${diffHr}h ago`;
  const diffDay = Math.floor(diffHr / 24);
  if (diffDay < 7) return `${diffDay}d ago`;
  return date.toLocaleDateString();
}

export function DocumentItem({ doc, isSelected, onSelect, onDelete }: DocumentItemProps) {
  return (
    <div
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
            {doc.pageCount} {doc.pageCount === 1 ? "page" : "pages"}
            {doc.fileSize ? ` · ${formatFileSize(doc.fileSize)}` : ""}
            {doc.uploadedAt ? ` · ${formatRelativeTime(doc.uploadedAt)}` : ""}
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
}
