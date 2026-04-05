import { useState, useEffect, useCallback, useRef } from "react";
import { AppLayout } from "@/components/layout/AppLayout";
import { ChatPanel } from "@/components/chat/ChatPanel";
import { PdfViewerPanel } from "@/components/pdf/PdfViewerPanel";
import { UploadDropzone } from "@/components/upload/UploadDropzone";
import { DocumentList } from "@/components/upload/DocumentList";
import { useDocumentChat } from "@/hooks/useDocumentChat";
import { useIsMobile } from "@/hooks/useMediaQuery";
import { uploadDocument, getDocuments, deleteDocument, getEngines } from "@/lib/api";
import type { DocumentDto, ChatMessage, Citation, EngineInfo } from "@/lib/types";
import { EngineSelector } from "@/components/ui/engine-selector";
import { FileSearch } from "lucide-react";
import type { MobileTab } from "@/components/layout/MobileTabBar";

export function App() {
  const [documents, setDocuments] = useState<DocumentDto[]>([]);
  const [documentsLoading, setDocumentsLoading] = useState(true);
  const [selectedDoc, setSelectedDoc] = useState<DocumentDto | null>(null);
  const [activeCitation, setActiveCitation] = useState<Citation | null>(null);
  const [engines, setEngines] = useState<EngineInfo[]>([]);
  const [selectedEngine, setSelectedEngine] = useState<string>("rag");
  const { messages, isLoading, sendMessage, clearMessages, restoreMessages } = useDocumentChat();
  const chatCacheRef = useRef<Map<string, ChatMessage[]>>(new Map());

  // Mobile state
  const [mobileTab, setMobileTab] = useState<MobileTab>("chat");
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const isMobile = useIsMobile();

  useEffect(() => {
    getDocuments()
      .then(setDocuments)
      .catch(console.error)
      .finally(() => setDocumentsLoading(false));
    getEngines()
      .then(setEngines)
      .catch(console.error);
  }, []);

  const handleUpload = useCallback(async (file: File) => {
    // Save current chat before switching to the new doc
    if (selectedDoc && messages.length > 0) {
      chatCacheRef.current.set(selectedDoc.id, messages);
    }
    const response = await uploadDocument(file);
    const docs = await getDocuments();
    setDocuments(docs);
    const newDoc = docs.find(d => d.id === response.id);
    if (newDoc) {
      setSelectedDoc(newDoc);
      setActiveCitation(null);
      clearMessages();
      setSidebarOpen(false);
    }
  }, [selectedDoc, messages, clearMessages]);

  const handleSelectDoc = useCallback((doc: DocumentDto) => {
    if (doc.id !== selectedDoc?.id) {
      // Save current chat history before switching
      if (selectedDoc) {
        chatCacheRef.current.set(selectedDoc.id, messages);
      }
      setSelectedDoc(doc);
      setActiveCitation(null);
      // Restore cached chat or start fresh
      const cached = chatCacheRef.current.get(doc.id);
      if (cached && cached.length > 0) {
        restoreMessages(cached);
      } else {
        clearMessages();
      }
      setSidebarOpen(false);
      setMobileTab("chat");
    }
  }, [selectedDoc, messages, clearMessages, restoreMessages]);

  const handleSendMessage = useCallback((question: string) => {
    if (!selectedDoc) return;
    sendMessage(question, selectedDoc.id, selectedEngine);
  }, [selectedDoc, selectedEngine, sendMessage]);

  const handleDeleteDoc = useCallback(async (doc: DocumentDto) => {
    if (!window.confirm(`Delete "${doc.fileName}"? This cannot be undone.`)) return;
    try {
      await deleteDocument(doc.id);
      chatCacheRef.current.delete(doc.id);
      const docs = await getDocuments();
      setDocuments(docs);
      if (selectedDoc?.id === doc.id) {
        setSelectedDoc(null);
        setActiveCitation(null);
        clearMessages();
      }
    } catch (err) {
      console.error("Failed to delete document:", err);
    }
  }, [selectedDoc, clearMessages]);

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
      {/* Header - only in sidebar on mobile */}
      <div className="px-4 py-3 bg-white border-b flex items-center gap-2.5">
        <div className="flex items-center justify-center h-8 w-8 rounded-lg bg-primary text-primary-foreground shrink-0">
          <FileSearch className="h-4.5 w-4.5" />
        </div>
        <div>
          <h1 className="text-base font-semibold tracking-tight leading-none">Ask My PDF</h1>
          <p className="text-xs md:text-[11px] text-muted-foreground mt-0.5">Document Q&A with AI</p>
        </div>
      </div>

      {/* Upload + Engine */}
      <div className="px-4 pt-4 pb-3 space-y-4 shrink-0">
        <UploadDropzone onUpload={handleUpload} />
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
            onSelect={handleSelectDoc}
            onDelete={handleDeleteDoc}
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
      {/* Header */}
      <div className="px-4 py-3 bg-white border-b flex items-center gap-2.5">
        <div className="flex items-center justify-center h-8 w-8 rounded-lg bg-primary text-primary-foreground shrink-0">
          <FileSearch className="h-4.5 w-4.5" />
        </div>
        <div>
          <h1 className="text-base font-semibold tracking-tight leading-none">Ask My PDF</h1>
          <p className="text-[11px] text-muted-foreground mt-0.5">Document Q&A with AI</p>
        </div>
      </div>

      {/* Setup (fixed) */}
      <div className="px-4 pt-4 pb-3 space-y-4 shrink-0">
        <UploadDropzone onUpload={handleUpload} />
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
            onSelect={handleSelectDoc}
            onDelete={handleDeleteDoc}
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
