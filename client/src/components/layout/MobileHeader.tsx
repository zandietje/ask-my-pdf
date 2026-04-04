import { Menu, FileSearch } from "lucide-react";
import { Button } from "@/components/ui/button";

interface MobileHeaderProps {
  documentName: string | null;
  onOpenSidebar: () => void;
}

export function MobileHeader({ documentName, onOpenSidebar }: MobileHeaderProps) {
  return (
    <div className="flex items-center gap-3 px-4 py-3 bg-white border-b shrink-0">
      <Button
        variant="ghost"
        size="icon"
        onClick={onOpenSidebar}
        className="h-10 w-10 shrink-0"
      >
        <Menu className="h-5 w-5" />
      </Button>
      <div className="flex items-center gap-2.5 min-w-0 flex-1">
        <div className="flex items-center justify-center h-8 w-8 rounded-lg bg-primary text-primary-foreground shrink-0">
          <FileSearch className="h-4.5 w-4.5" />
        </div>
        <div className="min-w-0">
          <h1 className="text-base font-semibold tracking-tight leading-none">Ask My PDF</h1>
          {documentName && (
            <p className="text-xs text-muted-foreground mt-0.5 truncate">{documentName}</p>
          )}
        </div>
      </div>
    </div>
  );
}
