import { useState, useEffect, useCallback, useRef } from "react";
import { AppLayout } from "@/components/layout/AppLayout";
import { ChatPanel } from "@/components/chat/ChatPanel";
import { PdfViewerPanel } from "@/components/pdf/PdfViewerPanel";
import { UploadDropzone } from "@/components/upload/UploadDropzone";
import { DocumentList } from "@/components/upload/DocumentList";
import { useDocumentChat } from "@/hooks/useDocumentChat";
import { uploadDocument, getDocuments, deleteDocument } from "@/lib/api";
import type { DocumentDto, ChatMessage, Citation } from "@/lib/types";
import { FileSearch } from "lucide-react";

export function App() {
  const [documents, setDocuments] = useState<DocumentDto[]>([]);
  const [documentsLoading, setDocumentsLoading] = useState(true);
  const [selectedDoc, setSelectedDoc] = useState<DocumentDto | null>(null);
  const [activeCitation, setActiveCitation] = useState<Citation | null>(null);
  const { messages, isLoading, sendMessage, clearMessages, restoreMessages } = useDocumentChat();
  const chatCacheRef = useRef<Map<string, ChatMessage[]>>(new Map());

  useEffect(() => {
    getDocuments()
      .then(setDocuments)
      .catch(console.error)
      .finally(() => setDocumentsLoading(false));
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
    }
  }, [selectedDoc, messages, clearMessages, restoreMessages]);

  const handleSendMessage = useCallback((question: string) => {
    if (!selectedDoc) return;
    sendMessage(question, selectedDoc.id);
  }, [selectedDoc, sendMessage]);

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
    // Spread into a new object so React detects the change even on repeated clicks
    setActiveCitation({ ...citation });
  }, []);

  const leftPanel = (
    <div className="flex flex-col h-full overflow-hidden bg-slate-50/50">
      <div className="p-4 border-b bg-white space-y-3">
        <div className="flex items-center gap-2">
          <div className="flex items-center justify-center h-8 w-8 rounded-lg bg-primary text-primary-foreground">
            <FileSearch className="h-4.5 w-4.5" />
          </div>
          <div>
            <h1 className="text-base font-semibold tracking-tight">Ask My PDF</h1>
            <p className="text-[11px] text-muted-foreground -mt-0.5">Document Q&A with AI</p>
          </div>
        </div>
        <UploadDropzone onUpload={handleUpload} />
        <DocumentList
          documents={documents}
          selectedId={selectedDoc?.id ?? null}
          onSelect={handleSelectDoc}
          onDelete={handleDeleteDoc}
          isLoading={documentsLoading}
        />
      </div>
      <div className="flex-1 min-h-0 overflow-hidden">
        <ChatPanel
          messages={messages}
          isLoading={isLoading}
          hasDocument={selectedDoc !== null}
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
    <div className="flex h-full flex-col items-center justify-center bg-slate-50 text-muted-foreground gap-3">
      <FileSearch className="h-12 w-12 text-muted-foreground/30" />
      <div className="text-center">
        <p className="text-sm font-medium">No document selected</p>
        <p className="text-xs mt-0.5">Upload and select a PDF to view it here</p>
      </div>
    </div>
  );

  return <AppLayout leftPanel={leftPanel} rightPanel={rightPanel} />;
}
