import { useState } from "react";
import type { ChatMessage, Citation } from "@/lib/types";
import { MessageList } from "./MessageList";
import { ChatInput } from "./ChatInput";
import { ChatEmptyState } from "./ChatEmptyState";

interface ChatPanelProps {
  messages: ChatMessage[];
  isLoading: boolean;
  hasDocument: boolean;
  documentName: string | null;
  onSendMessage: (question: string) => void;
  onCitationClick: (citation: Citation) => void;
}

export function ChatPanel({ messages, isLoading, hasDocument, documentName, onSendMessage, onCitationClick }: ChatPanelProps) {
  const [input, setInput] = useState("");

  const handleSend = () => {
    if (!input.trim() || isLoading || !hasDocument) return;
    onSendMessage(input.trim());
    setInput("");
  };

  return (
    <div className="flex flex-col h-full">
      {messages.length === 0 ? (
        <ChatEmptyState
          hasDocument={hasDocument}
          documentName={documentName}
          onSuggestion={(q) => { if (!isLoading && hasDocument) onSendMessage(q); }}
        />
      ) : (
        <MessageList
          messages={messages}
          onCitationClick={onCitationClick}
        />
      )}

      <ChatInput
        value={input}
        onChange={setInput}
        onSend={handleSend}
        disabled={!hasDocument || isLoading}
        placeholder={hasDocument ? "Ask about this document..." : "Select a document first"}
        documentName={documentName}
        hasDocument={hasDocument}
      />
    </div>
  );
}
