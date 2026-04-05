import { useState, useEffect, useCallback } from "react";
import { AppLayout } from "@/components/layout/AppLayout";
import { ChatPanel } from "@/components/chat/ChatPanel";
import { PdfViewerPanel } from "@/components/pdf/PdfViewerPanel";
import { UploadDropzone } from "@/components/upload/UploadDropzone";
import { DocumentList } from "@/components/upload/DocumentList";
import { useDocumentChat } from "@/hooks/useDocumentChat";
import { useDocumentManager } from "@/hooks/useDocumentManager";
import { useIsMobile } from "@/hooks/useMediaQuery";
import { getEngines } from "@/lib/api";
import type { Citation, EngineInfo } from "@/lib/types";
import { EngineSelector } from "@/components/ui/engine-selector";
import { FileSearch } from "lucide-react";
import { SidebarHeader } from "@/components/layout/SidebarHeader";
import type { MobileTab } from "@/components/layout/MobileTabBar";

export function App() {
  const { messages, isLoading, sendMessage, clearMessages, restoreMessages } = useDocumentChat();

  const {
    documents, documentsLoading, selectedDoc,
    handleUpload, handleSelectDoc, handleDeleteDoc,
  } = useDocumentManager(messages, clearMessages, restoreMessages);

  const [activeCitation, setActiveCitation] = useState<Citation | null>(null);
  const [engines, setEngines] = useState<EngineInfo[]>([]);
  const [selectedEngine, setSelectedEngine] = useState<string>("rag");

  // Mobile state
  const [mobileTab, setMobileTab] = useState<MobileTab>("chat");
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const isMobile = useIsMobile();

  useEffect(() => {
    getEngines()
      .then(setEngines)
      .catch(console.error);
  }, []);

  const onUpload = useCallback(async (file: File) => {
    const newDoc = await handleUpload(file);
    if (newDoc) {
      setActiveCitation(null);
      setSidebarOpen(false);
    }
  }, [handleUpload]);

  const onSelectDoc = useCallback((doc: Parameters<typeof handleSelectDoc>[0]) => {
    const switched = handleSelectDoc(doc);
    if (switched) {
      setActiveCitation(null);
      setSidebarOpen(false);
      setMobileTab("chat");
    }
  }, [handleSelectDoc]);

  const handleSendMessage = useCallback((question: string) => {
    if (!selectedDoc) return;
    sendMessage(question, selectedDoc.id, selectedEngine);
  }, [selectedDoc, selectedEngine, sendMessage]);

  const onDeleteDoc = useCallback(async (doc: Parameters<typeof handleDeleteDoc>[0]) => {
    await handleDeleteDoc(doc);
    if (selectedDoc?.id === doc.id) {
      setActiveCitation(null);
    }
  }, [handleDeleteDoc, selectedDoc]);

  const handleCitationClick = useCallback((citation: Citation) => {
    setActiveCitation({ ...citation });
    // On mobile, switch to PDF tab so user sees the highlight
    if (isMobile) {
      setMobileTab("pdf");
    }
  }, [isMobile]);

  // Sidebar content (used inside Sheet on mobile, inline on desktop)
  const sidebarContent = (
    <div className="flex flex-col h-full bg-slate-50/50">
      <SidebarHeader />

      {/* Upload + Engine */}
      <div className="px-4 pt-4 pb-3 space-y-4 shrink-0">
        <UploadDropzone onUpload={onUpload} />
        <EngineSelector
          engines={engines}
          selected={selectedEngine}
          onChange={setSelectedEngine}
        />
      </div>

      {/* Documents */}
      <div className="px-4 pb-2 flex flex-col min-h-0 flex-1">
        <label className="text-xs md:text-[11px] font-medium text-muted-foreground uppercase tracking-wider mb-1.5 shrink-0">
          Documents
        </label>
        <div className="overflow-y-auto flex-1">
          <DocumentList
            documents={documents}
            selectedId={selectedDoc?.id ?? null}
            onSelect={onSelectDoc}
            onDelete={onDeleteDoc}
            isLoading={documentsLoading}
          />
        </div>
      </div>
    </div>
  );

  // Desktop left panel = sidebar + chat stacked
  const leftPanel = isMobile ? (
    // Mobile: left panel is just the chat (no header, no sidebar)
    <div className="flex flex-col h-full overflow-hidden">
      <ChatPanel
        messages={messages}
        isLoading={isLoading}
        hasDocument={selectedDoc !== null}
        documentName={selectedDoc?.fileName ?? null}
        onSendMessage={handleSendMessage}
        onCitationClick={handleCitationClick}
      />
    </div>
  ) : (
    // Desktop: full left panel with sidebar + chat
    <div className="flex flex-col h-full overflow-hidden bg-slate-50/50">
      <SidebarHeader />

      {/* Setup (fixed) */}
      <div className="px-4 pt-4 pb-3 space-y-4 shrink-0">
        <UploadDropzone onUpload={onUpload} />
        <EngineSelector
          engines={engines}
          selected={selectedEngine}
          onChange={setSelectedEngine}
        />
      </div>

      {/* Documents (scrollable) */}
      <div className="px-4 pb-2 flex flex-col min-h-0 shrink-0 border-b" style={{ maxHeight: "35%" }}>
        <label className="text-[11px] font-medium text-muted-foreground uppercase tracking-wider mb-1.5 shrink-0">
          Documents
        </label>
        <div className="overflow-y-auto">
          <DocumentList
            documents={documents}
            selectedId={selectedDoc?.id ?? null}
            onSelect={onSelectDoc}
            onDelete={onDeleteDoc}
            isLoading={documentsLoading}
          />
        </div>
      </div>

      {/* Chat */}
      <div className="flex-1 min-h-0 overflow-hidden">
        <ChatPanel
          messages={messages}
          isLoading={isLoading}
          hasDocument={selectedDoc !== null}
          documentName={selectedDoc?.fileName ?? null}
          onSendMessage={handleSendMessage}
          onCitationClick={handleCitationClick}
        />
      </div>
    </div>
  );

  const rightPanel = selectedDoc ? (
    <PdfViewerPanel
      documentId={selectedDoc.id}
      activeCitation={activeCitation}
    />
  ) : (
    <div className="flex h-full flex-col items-center justify-center bg-slate-50 text-muted-foreground gap-4">
      <div className="flex items-center justify-center h-16 w-16 rounded-2xl bg-muted">
        <FileSearch className="h-8 w-8 text-muted-foreground/40" />
      </div>
      <div className="text-center space-y-1">
        <p className="text-sm font-medium text-foreground/70">No document selected</p>
        <p className="text-xs text-muted-foreground">Upload a PDF and select it to view here</p>
      </div>
    </div>
  );

  return (
    <AppLayout
      leftPanel={leftPanel}
      rightPanel={rightPanel}
      mobileTab={mobileTab}
      onMobileTabChange={setMobileTab}
      sidebarOpen={sidebarOpen}
      onSidebarOpenChange={setSidebarOpen}
      sidebarContent={sidebarContent}
      documentName={selectedDoc?.fileName ?? null}
      hasDocument={selectedDoc !== null}
    />
  );
}
