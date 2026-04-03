import { useState, useEffect, useCallback } from "react";
import { AppLayout } from "@/components/layout/AppLayout";
import { ChatPanel } from "@/components/chat/ChatPanel";
import { PdfViewerPanel } from "@/components/pdf/PdfViewerPanel";
import { UploadDropzone } from "@/components/upload/UploadDropzone";
import { DocumentList } from "@/components/upload/DocumentList";
import { useDocumentChat } from "@/hooks/useDocumentChat";
import { uploadDocument, getDocuments } from "@/lib/api";
import type { DocumentDto, Citation } from "@/lib/types";

export function App() {
  const [documents, setDocuments] = useState<DocumentDto[]>([]);
  const [selectedDoc, setSelectedDoc] = useState<DocumentDto | null>(null);
  const [activeCitation, setActiveCitation] = useState<Citation | null>(null);
  const { messages, isLoading, sendMessage, clearMessages } = useDocumentChat();

  useEffect(() => {
    getDocuments().then(setDocuments).catch(console.error);
  }, []);

  const handleUpload = useCallback(async (file: File) => {
    const response = await uploadDocument(file);
    const docs = await getDocuments();
    setDocuments(docs);
    const newDoc = docs.find(d => d.id === response.id);
    if (newDoc) setSelectedDoc(newDoc);
  }, []);

  const handleSelectDoc = useCallback((doc: DocumentDto) => {
    if (doc.id !== selectedDoc?.id) {
      setSelectedDoc(doc);
      setActiveCitation(null);
      clearMessages();
    }
  }, [selectedDoc, clearMessages]);

  const handleSendMessage = useCallback((question: string) => {
    if (!selectedDoc) return;
    sendMessage(question, selectedDoc.id);
  }, [selectedDoc, sendMessage]);

  const handleCitationClick = useCallback((citation: Citation) => {
    setActiveCitation(citation);
  }, []);

  const leftPanel = (
    <div className="flex flex-col h-full">
      <div className="p-4 border-b space-y-3">
        <h1 className="text-lg font-semibold">AskMyPdf</h1>
        <UploadDropzone onUpload={handleUpload} />
        <DocumentList
          documents={documents}
          selectedId={selectedDoc?.id ?? null}
          onSelect={handleSelectDoc}
        />
      </div>
      <div className="flex-1 overflow-hidden">
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
    <div className="flex h-full items-center justify-center bg-muted/30 text-muted-foreground">
      <p>Upload and select a document to view it here</p>
    </div>
  );

  return <AppLayout leftPanel={leftPanel} rightPanel={rightPanel} />;
}
