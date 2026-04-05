import { useIsMobile } from "@/hooks/useMediaQuery";
import { MobileTabBar, type MobileTab } from "./MobileTabBar";
import { TopBar } from "./TopBar";
import { Sidebar, SidebarExpandButton } from "./Sidebar";
import { ContentArea } from "./ContentArea";
import { Sheet, SheetContent } from "@/components/ui/sheet";
import type { EngineInfo } from "@/lib/types";

interface AppLayoutProps {
  chatPanel: React.ReactNode;
  pdfPanel: React.ReactNode;
  sidebarContent: React.ReactNode;
  // Engine selector
  engines: EngineInfo[];
  selectedEngine: string;
  onEngineChange: (key: string) => void;
  // Theme
  theme: "light" | "dark";
  onToggleTheme: () => void;
  // Sidebar
  sidebarCollapsed: boolean;
  onToggleSidebar: () => void;
  // Mobile
  mobileTab: MobileTab;
  onMobileTabChange: (tab: MobileTab) => void;
  sidebarOpen: boolean;
  onSidebarOpenChange: (open: boolean) => void;
  documentName: string | null;
  hasDocument: boolean;
}

export function AppLayout({
  chatPanel,
  pdfPanel,
  sidebarContent,
  engines,
  selectedEngine,
  onEngineChange,
  theme,
  onToggleTheme,
  sidebarCollapsed,
  onToggleSidebar,
  mobileTab,
  onMobileTabChange,
  sidebarOpen,
  onSidebarOpenChange,
  documentName,
  hasDocument,
}: AppLayoutProps) {
  const isMobile = useIsMobile();

  if (!isMobile) {
    return (
      <div className="flex flex-col h-screen">
        <TopBar
          engines={engines}
          selectedEngine={selectedEngine}
          onEngineChange={onEngineChange}
          theme={theme}
          onToggleTheme={onToggleTheme}
        />
        <div className="flex flex-1 min-h-0">
          <Sidebar
            isCollapsed={sidebarCollapsed}
            onToggleCollapse={onToggleSidebar}
          >
            {sidebarContent}
          </Sidebar>
          <div className="flex-1 min-w-0 relative">
            {sidebarCollapsed && (
              <SidebarExpandButton onClick={onToggleSidebar} />
            )}
            <ContentArea
              chatPanel={chatPanel}
              pdfPanel={pdfPanel}
            />
          </div>
        </div>
      </div>
    );
  }

  // Mobile layout
  return (
    <div className="flex flex-col h-[100dvh]">
      <TopBar
        engines={engines}
        selectedEngine={selectedEngine}
        onEngineChange={onEngineChange}
        theme={theme}
        onToggleTheme={onToggleTheme}
        isMobile
        onOpenSidebar={() => onSidebarOpenChange(true)}
        documentName={documentName}
      />

      <div className="flex-1 min-h-0 overflow-hidden relative">
        <div className={mobileTab === "chat" ? "h-full" : "h-full absolute inset-0 invisible"}>
          {chatPanel}
        </div>
        <div className={mobileTab === "pdf" ? "h-full" : "h-full absolute inset-0 invisible"}>
          {pdfPanel}
        </div>
      </div>

      <MobileTabBar
        activeTab={mobileTab}
        onTabChange={onMobileTabChange}
        hasDocument={hasDocument}
      />

      <Sheet open={sidebarOpen} onOpenChange={onSidebarOpenChange}>
        <SheetContent side="left" className="w-[85vw] max-w-sm p-0 overflow-y-auto">
          {sidebarContent}
        </SheetContent>
      </Sheet>
    </div>
  );
}
