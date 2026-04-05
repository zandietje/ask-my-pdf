import { FileSearch } from "lucide-react";

export function PdfEmptyState() {
  return (
    <div className="flex h-full flex-col items-center justify-center bg-background text-muted-foreground gap-4 p-6">
      <div className="h-16 w-16 rounded-2xl bg-secondary flex items-center justify-center">
        <FileSearch className="h-8 w-8 text-muted-foreground/40" />
      </div>
      <div className="text-center space-y-1.5">
        <p className="text-sm font-medium text-foreground/70">No document selected</p>
        <p className="text-xs text-muted-foreground">
          Upload and select a PDF to preview it here
        </p>
      </div>
    </div>
  );
}
