import { motion } from "framer-motion";
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
    <div className="flex border-t bg-card shrink-0 safe-area-bottom relative">
      {/* Sliding top indicator */}
      <motion.div
        className="absolute top-0 h-0.5 bg-primary"
        animate={{
          left: activeTab === "chat" ? "0%" : "50%",
          width: "50%",
        }}
        transition={{ type: "spring", stiffness: 400, damping: 35 }}
      />
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
