import { motion } from "framer-motion";
import { PanelLeftClose, PanelLeft } from "lucide-react";
import { Button } from "@/components/ui/button";

interface SidebarProps {
  isCollapsed: boolean;
  onToggleCollapse: () => void;
  children: React.ReactNode;
}

export function Sidebar({ isCollapsed, onToggleCollapse, children }: SidebarProps) {
  return (
    <motion.aside
      className="border-r bg-card shrink-0 flex flex-col overflow-hidden"
      animate={{
        width: isCollapsed ? 0 : 288,
        opacity: isCollapsed ? 0 : 1,
        borderRightWidth: isCollapsed ? 0 : 1,
      }}
      transition={{ duration: 0.25, ease: "easeInOut" }}
    >
      <div className="flex flex-col flex-1 min-h-0 min-w-[18rem]">
        {children}

        {/* Collapse button at bottom */}
        <div className="border-t p-2 shrink-0">
          <Button
            variant="ghost"
            size="sm"
            onClick={onToggleCollapse}
            className="w-full justify-start gap-2 text-muted-foreground hover:text-foreground h-8 text-xs"
          >
            <PanelLeftClose className="h-4 w-4" />
            <span>Collapse sidebar</span>
          </Button>
        </div>
      </div>
    </motion.aside>
  );
}

/** Small button shown when sidebar is collapsed to re-expand it */
export function SidebarExpandButton({ onClick }: { onClick: () => void }) {
  return (
    <Button
      variant="ghost"
      size="icon"
      onClick={onClick}
      className="absolute left-2 top-2 z-10 h-8 w-8 rounded-lg bg-card border shadow-sm hover:bg-surface-hover"
      title="Expand sidebar"
    >
      <PanelLeft className="h-4 w-4" />
    </Button>
  );
}
