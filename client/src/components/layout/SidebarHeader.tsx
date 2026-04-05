import { FileSearch } from "lucide-react";

export function SidebarHeader() {
  return (
    <div className="px-4 py-3 bg-white border-b flex items-center gap-2.5">
      <div className="flex items-center justify-center h-8 w-8 rounded-lg bg-primary text-primary-foreground shrink-0">
        <FileSearch className="h-4.5 w-4.5" />
      </div>
      <div>
        <h1 className="text-base font-semibold tracking-tight leading-none">Ask My PDF</h1>
        <p className="text-xs md:text-[11px] text-muted-foreground mt-0.5">Document Q&A with AI</p>
      </div>
    </div>
  );
}
