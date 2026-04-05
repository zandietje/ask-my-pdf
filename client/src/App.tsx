import { useState, useEffect, useCallback, useRef } from "react";
import { Toaster } from "sonner";
import { AppLayout } from "@/components/layout/AppLayout";
import { ChatPanel } from "@/components/chat/ChatPanel";
import { PdfViewerPanel } from "@/components/pdf/PdfViewerPanel";
import { UploadDropzone } from "@/components/upload/UploadDropzone";
import { DocumentList } from "@/components/upload/DocumentList";
import { useDocumentChat } from "@/hooks/useDocumentChat";
import { useDocumentManager } from "@/hooks/useDocumentManager";
import { useIsMobile, useMediaQuery } from "@/hooks/useMediaQuery";
import { useTheme } from "@/hooks/useTheme";
import { getEngines } from "@/lib/api";
import type { Citation, EngineInfo } from "@/lib/types";
import { PdfEmptyState } from "@/components/pdf/PdfEmptyState";
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

  // Layout state
  const [sidebarCollapsed, setSidebarCollapsed] = useState(false);
  const { theme, toggleTheme } = useTheme();

  // Mobile state
  const [mobileTab, setMobileTab] = useState<MobileTab>("chat");
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const isMobile = useIsMobile();

  // Auto-collapse sidebar on narrow desktops (1024-1279px)
  const isNarrowDesktop = useMediaQuery("(max-width: 1279px)");
  const userManuallyExpanded = useRef(false);

  useEffect(() => {
    if (isNarrowDesktop && !isMobile && !userManuallyExpanded.current) {
      setSidebarCollapsed(true);
    }
  }, [isNarrowDesktop, isMobile]);

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
    if (isMobile) {
      setMobileTab("pdf");
    }
  }, [isMobile]);

  // Sidebar content (used inside Sidebar on desktop, Sheet on mobile)
  const sidebarContent = (
    <div className="flex flex-col h-full">
      <div className="px-4 pt-4 pb-3 space-y-4 shrink-0">
        <UploadDropzone onUpload={onUpload} />
      </div>

      <div className="px-4 pb-2 flex flex-col min-h-0 flex-1">
        <label className="text-xs font-medium text-muted-foreground uppercase tracking-wider mb-1.5 shrink-0">
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

  const chatPanel = (
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
  );

  const pdfPanel = selectedDoc ? (
    <PdfViewerPanel
      documentId={selectedDoc.id}
      activeCitation={activeCitation}
    />
  ) : (
    <PdfEmptyState />
  );

  return (
    <>
      <AppLayout
        chatPanel={chatPanel}
        pdfPanel={pdfPanel}
        sidebarContent={sidebarContent}
        engines={engines}
        selectedEngine={selectedEngine}
        onEngineChange={setSelectedEngine}
        theme={theme}
        onToggleTheme={toggleTheme}
        sidebarCollapsed={sidebarCollapsed}
        onToggleSidebar={() => {
          setSidebarCollapsed(prev => {
            if (prev) userManuallyExpanded.current = true;
            return !prev;
          });
        }}
        mobileTab={mobileTab}
        onMobileTabChange={setMobileTab}
        sidebarOpen={sidebarOpen}
        onSidebarOpenChange={setSidebarOpen}
        documentName={selectedDoc?.fileName ?? null}
        hasDocument={selectedDoc !== null}
      />
      <Toaster position="top-right" richColors closeButton />
    </>
  );
}
