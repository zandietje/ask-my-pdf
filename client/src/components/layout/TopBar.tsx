import { FileSearch, Menu } from "lucide-react";
import { Button } from "@/components/ui/button";
import { ThemeToggle } from "@/components/ui/theme-toggle";
import { EngineSelector } from "@/components/ui/engine-selector";
import type { EngineInfo } from "@/lib/types";

interface TopBarProps {
  engines: EngineInfo[];
  selectedEngine: string;
  onEngineChange: (key: string) => void;
  theme: "light" | "dark";
  onToggleTheme: () => void;
  isMobile?: boolean;
  onOpenSidebar?: () => void;
  documentName?: string | null;
}

export function TopBar({
  engines,
  selectedEngine,
  onEngineChange,
  theme,
  onToggleTheme,
  isMobile,
  onOpenSidebar,
}: TopBarProps) {
  return (
    <div className="h-14 border-b bg-card flex items-center px-3 md:px-4 gap-2 md:gap-4 shrink-0">
      {/* Left: hamburger (mobile) + branding */}
      <div className="flex items-center gap-2 md:gap-2.5 shrink-0">
        {isMobile && onOpenSidebar && (
          <Button
            variant="ghost"
            size="icon"
            onClick={onOpenSidebar}
            className="h-8 w-8 shrink-0 -ml-1"
          >
            <Menu className="h-5 w-5" />
          </Button>
        )}
        <div className="flex items-center justify-center h-8 w-8 rounded-lg bg-primary text-primary-foreground shrink-0">
          <FileSearch className="h-4 w-4" />
        </div>
        <span className="text-base font-semibold tracking-tight hidden sm:inline">Ask My PDF</span>
      </div>

      {/* Center: engine selector */}
      <div className="flex-1 flex justify-center min-w-0">
        <EngineSelector
          engines={engines}
          selected={selectedEngine}
          onChange={onEngineChange}
        />
      </div>

      {/* Right: theme toggle */}
      <div className="shrink-0">
        <ThemeToggle theme={theme} onToggle={onToggleTheme} />
      </div>
    </div>
  );
}
