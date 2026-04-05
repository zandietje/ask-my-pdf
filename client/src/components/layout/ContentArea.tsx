import { Panel, PanelGroup, PanelResizeHandle } from "react-resizable-panels";

interface ContentAreaProps {
  chatPanel: React.ReactNode;
  pdfPanel: React.ReactNode;
}

export function ContentArea({ chatPanel, pdfPanel }: ContentAreaProps) {
  return (
    <PanelGroup direction="horizontal" className="flex-1">
      <Panel defaultSize={45} minSize={25}>
        {chatPanel}
      </Panel>
      <PanelResizeHandle className="relative flex items-center justify-center w-3 group cursor-col-resize">
        <div className="h-8 w-1 rounded-full bg-border group-hover:bg-primary/40 group-active:bg-primary/60 transition-colors flex flex-col items-center justify-center gap-0.5">
          <span className="block h-0.5 w-0.5 rounded-full bg-muted-foreground/30 group-hover:bg-primary/60" />
          <span className="block h-0.5 w-0.5 rounded-full bg-muted-foreground/30 group-hover:bg-primary/60" />
          <span className="block h-0.5 w-0.5 rounded-full bg-muted-foreground/30 group-hover:bg-primary/60" />
        </div>
      </PanelResizeHandle>
      <Panel defaultSize={55} minSize={30}>
        {pdfPanel}
      </Panel>
    </PanelGroup>
  );
}
