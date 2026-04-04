import { useState, useRef, useEffect } from "react";
import type { ChatMessage, Citation } from "@/lib/types";
import { MessageBubble } from "./MessageBubble";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";
import { Send, MessageSquareText, FileText, ArrowRight } from "lucide-react";

const SUGGESTED_QUESTIONS = [
  "What is this document about?",
  "Summarize the key points",
  "What are the main conclusions?",
];

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

  const handleSuggestion = (question: string) => {
    if (isLoading || !hasDocument) return;
    onSendMessage(question);
  };

  return (
    <div className="flex flex-col h-full">
      <div className="flex-1 overflow-y-auto p-4 space-y-3">
        {messages.length === 0 && (
          <div className="flex flex-col items-center justify-center h-full text-muted-foreground gap-4">
            <div className="flex items-center justify-center h-12 w-12 rounded-full bg-muted">
              <MessageSquareText className="h-6 w-6" />
            </div>
            <div className="text-center space-y-1">
              <p className="text-sm font-medium text-foreground/70">
                {hasDocument ? "Ready to answer" : "No document selected"}
              </p>
              <p className="text-xs">
                {hasDocument
                  ? `Ask any question about ${documentName ?? "the document"}`
                  : "Upload and select a PDF to get started"}
              </p>
            </div>
            {hasDocument && (
              <div className="flex flex-col gap-1.5 w-full max-w-[260px]">
                {SUGGESTED_QUESTIONS.map((q) => (
                  <button
                    key={q}
                    type="button"
                    onClick={() => handleSuggestion(q)}
                    className="flex items-center gap-2 text-left text-xs text-muted-foreground hover:text-foreground rounded-lg border border-border/60 hover:border-border hover:bg-accent/40 px-3 py-2 transition-colors group"
                  >
                    <span className="flex-1">{q}</span>
                    <ArrowRight className="h-3 w-3 shrink-0 opacity-0 group-hover:opacity-100 transition-opacity" />
                  </button>
                ))}
              </div>
            )}
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

      <div className="border-t bg-white shadow-[0_-1px_3px_rgba(0,0,0,0.04)]">
        {hasDocument && documentName && (
          <div className="px-4 pt-2.5 flex items-center gap-1.5 text-[11px] text-muted-foreground">
            <FileText className="h-3 w-3 shrink-0" />
            <span className="truncate">Asking about <span className="font-medium text-foreground/70">{documentName}</span></span>
          </div>
        )}
        <form onSubmit={handleSubmit} className="p-3 pt-2 flex gap-2">
          <Input
            value={input}
            onChange={e => setInput(e.target.value)}
            placeholder={hasDocument ? "Ask about this document..." : "Select a document first"}
            disabled={!hasDocument || isLoading}
            className="bg-slate-50 focus:bg-white transition-colors rounded-lg"
          />
          <Button
            type="submit"
            size="icon"
            disabled={!input.trim() || isLoading || !hasDocument}
            className="shrink-0 rounded-lg"
          >
            <Send className="h-4 w-4" />
          </Button>
        </form>
      </div>
    </div>
  );
}
