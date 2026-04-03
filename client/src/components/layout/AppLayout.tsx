import { Panel, PanelGroup, PanelResizeHandle } from "react-resizable-panels";

interface AppLayoutProps {
  leftPanel: React.ReactNode;
  rightPanel: React.ReactNode;
}

export function AppLayout({ leftPanel, rightPanel }: AppLayoutProps) {
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
