import { useState, useEffect, useCallback, useRef } from "react";
import { uploadDocument, getDocuments, deleteDocument } from "@/lib/api";
import type { DocumentDto, ChatMessage } from "@/lib/types";

export function useDocumentManager(
  messages: ChatMessage[],
  clearMessages: () => void,
  restoreMessages: (msgs: ChatMessage[]) => void,
) {
  const [documents, setDocuments] = useState<DocumentDto[]>([]);
  const [documentsLoading, setDocumentsLoading] = useState(true);
  const [selectedDoc, setSelectedDoc] = useState<DocumentDto | null>(null);
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
      clearMessages();
    }
    return newDoc ?? null;
  }, [selectedDoc, messages, clearMessages]);

  const handleSelectDoc = useCallback((doc: DocumentDto): boolean => {
    if (doc.id === selectedDoc?.id) return false;
    // Save current chat history before switching
    if (selectedDoc) {
      chatCacheRef.current.set(selectedDoc.id, messages);
    }
    setSelectedDoc(doc);
    // Restore cached chat or start fresh
    const cached = chatCacheRef.current.get(doc.id);
    if (cached && cached.length > 0) {
      restoreMessages(cached);
    } else {
      clearMessages();
    }
    return true;
  }, [selectedDoc, messages, clearMessages, restoreMessages]);

  const handleDeleteDoc = useCallback(async (doc: DocumentDto) => {
    if (!window.confirm(`Delete "${doc.fileName}"? This cannot be undone.`)) return;
    try {
      await deleteDocument(doc.id);
      chatCacheRef.current.delete(doc.id);
      const docs = await getDocuments();
      setDocuments(docs);
      if (selectedDoc?.id === doc.id) {
        setSelectedDoc(null);
        clearMessages();
      }
    } catch (err) {
      console.error("Failed to delete document:", err);
    }
  }, [selectedDoc, clearMessages]);

  return {
    documents, documentsLoading, selectedDoc,
    handleUpload, handleSelectDoc, handleDeleteDoc,
  };
}
