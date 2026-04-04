import { Panel, PanelGroup, PanelResizeHandle } from "react-resizable-panels";
import { useIsMobile } from "@/hooks/useMediaQuery";
import { MobileTabBar, type MobileTab } from "./MobileTabBar";
import { MobileHeader } from "./MobileHeader";
import { Sheet, SheetContent } from "@/components/ui/sheet";

interface AppLayoutProps {
  leftPanel: React.ReactNode;
  rightPanel: React.ReactNode;
  // Mobile-only props
  mobileTab: MobileTab;
  onMobileTabChange: (tab: MobileTab) => void;
  sidebarOpen: boolean;
  onSidebarOpenChange: (open: boolean) => void;
  sidebarContent: React.ReactNode;
  documentName: string | null;
  hasDocument: boolean;
}

export function AppLayout({
  leftPanel,
  rightPanel,
  mobileTab,
  onMobileTabChange,
  sidebarOpen,
  onSidebarOpenChange,
  sidebarContent,
  documentName,
  hasDocument,
}: AppLayoutProps) {
  const isMobile = useIsMobile();

  if (!isMobile) {
    return (
      <PanelGroup direction="horizontal" className="h-screen">
        <Panel defaultSize={40} minSize={25}>
          {leftPanel}
        </Panel>
        <PanelResizeHandle className="w-1 bg-border hover:bg-primary/30 active:bg-primary/40 transition-colors" />
        <Panel defaultSize={60} minSize={30}>
          {rightPanel}
        </Panel>
      </PanelGroup>
    );
  }

  return (
    <div className="flex flex-col h-[100dvh]">
      <MobileHeader
        documentName={documentName}
        onOpenSidebar={() => onSidebarOpenChange(true)}
      />

      <div className="flex-1 min-h-0 overflow-hidden relative">
        <div className={mobileTab === "chat" ? "h-full" : "h-full absolute inset-0 invisible"}>
          {leftPanel}
        </div>
        <div className={mobileTab === "pdf" ? "h-full" : "h-full absolute inset-0 invisible"}>
          {rightPanel}
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
