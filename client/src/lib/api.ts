import type { DocumentDto, UploadResponse } from "./types";

export async function uploadDocument(file: File): Promise<UploadResponse> {
  const formData = new FormData();
  formData.append("file", file);
  const res = await fetch("/api/documents/upload", {
    method: "POST",
    body: formData,
  });
  if (!res.ok) {
    const err = await res.json().catch(() => null);
    throw new Error(err?.error ?? "Upload failed");
  }
  return res.json();
}

export async function getDocuments(): Promise<DocumentDto[]> {
  const res = await fetch("/api/documents");
  if (!res.ok) throw new Error("Failed to fetch documents");
  return res.json();
}

export function getDocumentFileUrl(documentId: string): string {
  return `/api/documents/${documentId}/file`;
}

export async function deleteDocument(documentId: string): Promise<void> {
  const res = await fetch(`/api/documents/${documentId}`, { method: "DELETE" });
  if (!res.ok) throw new Error("Failed to delete document");
}
