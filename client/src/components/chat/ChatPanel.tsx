import { useState, useRef, useEffect } from "react";
import type { ChatMessage, Citation } from "@/lib/types";
import { MessageBubble } from "./MessageBubble";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";
import { Send, MessageSquare } from "lucide-react";

interface ChatPanelProps {
  messages: ChatMessage[];
  isLoading: boolean;
  hasDocument: boolean;
  onSendMessage: (question: string) => void;
  onCitationClick: (citation: Citation) => void;
}

export function ChatPanel({ messages, isLoading, hasDocument, onSendMessage, onCitationClick }: ChatPanelProps) {
  const [input, setInput] = useState("");
  const scrollRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    scrollRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages]);

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!input.trim() || isLoading || !hasDocument) return;
    onSendMessage(input.trim());
    setInput("");
  };

  return (
    <div className="flex flex-col h-full">
      <div className="flex-1 overflow-y-auto p-4 space-y-3">
        {messages.length === 0 && (
          <div className="flex flex-col items-center justify-center h-full text-muted-foreground gap-2">
            <MessageSquare className="h-8 w-8" />
            <p className="text-sm">
              {hasDocument
                ? "Ask a question about the document"
                : "Select a document to get started"}
            </p>
          </div>
        )}
        {messages.map(msg => (
          <MessageBubble
            key={msg.id}
            message={msg}
            onCitationClick={onCitationClick}
          />
        ))}
        <div ref={scrollRef} />
      </div>

      <form onSubmit={handleSubmit} className="border-t p-3 flex gap-2">
        <Input
          value={input}
          onChange={e => setInput(e.target.value)}
          placeholder={hasDocument ? "Ask a question..." : "Select a document first"}
          disabled={!hasDocument || isLoading}
        />
        <Button
          type="submit"
          size="icon"
          disabled={!input.trim() || isLoading || !hasDocument}
        >
          <Send className="h-4 w-4" />
        </Button>
      </form>
    </div>
  );
}
