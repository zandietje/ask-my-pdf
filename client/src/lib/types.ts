export interface DocumentDto {
  id: string;
  fileName: string;
  uploadedAt: string;
  pageCount: number;
  fileSize: number;
}

export interface UploadResponse {
  id: string;
  fileName: string;
  pageCount: number;
  fileSize: number;
}

export interface HighlightArea {
  pageIndex: number;
  left: number;
  top: number;
  width: number;
  height: number;
}

export interface Citation {
  documentId: string;
  documentName: string;
  pageNumber: number;
  citedText: string;
  highlightAreas: HighlightArea[];
}

export interface ChatMessage {
  id: string;
  role: "user" | "assistant";
  content: string;
  citations: Citation[];
  isStreaming: boolean;
}
