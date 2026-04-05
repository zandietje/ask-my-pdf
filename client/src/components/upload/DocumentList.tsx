import type { DocumentDto } from "@/lib/types";
import { FileText } from "lucide-react";
import { Skeleton } from "@/components/ui/skeleton";
import { DocumentItem } from "./DocumentItem";

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
      <div className="space-y-1">
        {[1, 2, 3].map(i => (
          <Skeleton key={i} className="h-11 w-full rounded-lg" />
        ))}
      </div>
    );
  }

  if (documents.length === 0) {
    return (
      <div className="flex flex-col items-center gap-2 py-6 text-center">
        <FileText className="h-5 w-5 text-muted-foreground/30" />
        <p className="text-xs text-muted-foreground">No documents uploaded yet</p>
      </div>
    );
  }

  return (
    <div className="space-y-0.5">
      {documents.map(doc => (
        <DocumentItem
          key={doc.id}
          doc={doc}
          isSelected={doc.id === selectedId}
          onSelect={onSelect}
          onDelete={onDelete}
        />
      ))}
    </div>
  );
}
