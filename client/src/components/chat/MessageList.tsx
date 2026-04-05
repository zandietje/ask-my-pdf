import { useRef, useState, useEffect } from "react";
import type { ChatMessage, Citation } from "@/lib/types";
import { MessageBubble } from "./MessageBubble";
import { ChevronDown } from "lucide-react";

interface MessageListProps {
  messages: ChatMessage[];
  onCitationClick: (citation: Citation) => void;
}

export function MessageList({ messages, onCitationClick }: MessageListProps) {
  const scrollContainerRef = useRef<HTMLDivElement>(null);
  const endRef = useRef<HTMLDivElement>(null);
  const [showScrollButton, setShowScrollButton] = useState(false);
  const prevMessageCount = useRef(messages.length);

  const handleScroll = () => {
    const el = scrollContainerRef.current;
    if (!el) return;
    const distanceFromBottom = el.scrollHeight - el.scrollTop - el.clientHeight;
    setShowScrollButton(distanceFromBottom > 200);
  };

  // Auto-scroll on new messages or streaming content if near bottom
  useEffect(() => {
    const el = scrollContainerRef.current;
    if (!el) return;
    const distanceFromBottom = el.scrollHeight - el.scrollTop - el.clientHeight;
    if (messages.length > prevMessageCount.current || distanceFromBottom < 200) {
      endRef.current?.scrollIntoView({ behavior: "smooth" });
    }
    prevMessageCount.current = messages.length;
  }, [messages]);

  const scrollToBottom = () => {
    endRef.current?.scrollIntoView({ behavior: "smooth" });
  };

  return (
    <div className="relative flex-1 min-h-0">
      <div
        ref={scrollContainerRef}
        onScroll={handleScroll}
        className="overflow-y-auto h-full p-4 md:p-4 space-y-4 md:space-y-3"
      >
        {messages.map((msg) => (
          <MessageBubble
            key={msg.id}
            message={msg}
            onCitationClick={onCitationClick}
          />
        ))}
        <div ref={endRef} />
      </div>

      {showScrollButton && (
        <button
          onClick={scrollToBottom}
          className="absolute bottom-3 left-1/2 -translate-x-1/2 bg-card border border-border shadow-md rounded-full px-3 py-1.5 flex items-center gap-1.5 text-xs text-muted-foreground hover:text-foreground transition-colors z-10"
        >
          <ChevronDown className="h-3.5 w-3.5" />
          <span>New messages</span>
        </button>
      )}
    </div>
  );
}
