import { MessageSquareText, FileText } from "lucide-react";
import { cn } from "@/lib/utils";

export type MobileTab = "chat" | "pdf";

interface MobileTabBarProps {
  activeTab: MobileTab;
  onTabChange: (tab: MobileTab) => void;
  hasDocument: boolean;
}

export function MobileTabBar({ activeTab, onTabChange, hasDocument }: MobileTabBarProps) {
  return (
    <div className="flex border-t bg-white shrink-0 safe-area-bottom">
      <button
        onClick={() => onTabChange("chat")}
        className={cn(
          "flex-1 flex flex-col items-center gap-1 py-2.5 text-xs font-medium transition-colors",
          activeTab === "chat"
            ? "text-primary"
            : "text-muted-foreground"
        )}
      >
        <MessageSquareText className="h-5 w-5" />
        <span>Chat</span>
      </button>
      <button
        onClick={() => onTabChange("pdf")}
        disabled={!hasDocument}
        className={cn(
          "flex-1 flex flex-col items-center gap-1 py-2.5 text-xs font-medium transition-colors",
          activeTab === "pdf"
            ? "text-primary"
            : "text-muted-foreground",
          !hasDocument && "opacity-40 pointer-events-none"
        )}
      >
        <FileText className="h-5 w-5" />
        <span>PDF</span>
      </button>
    </div>
  );
}
