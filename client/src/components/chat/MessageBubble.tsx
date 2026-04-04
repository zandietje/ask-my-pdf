import { useMemo } from "react";
import type { ChatMessage, Citation } from "@/lib/types";
import { CitationChip } from "./CitationChip";
import { cn } from "@/lib/utils";
import { Bot, User } from "lucide-react";

interface MessageBubbleProps {
  message: ChatMessage;
  onCitationClick: (citation: Citation) => void;
}

/** Merge citations on the same page into a single citation with combined highlight areas. */
function deduplicateCitations(citations: Citation[]): Citation[] {
  const byPage = new Map<string, Citation>();
  for (const c of citations) {
    const key = `${c.documentId}:${c.pageNumber}`;
    const existing = byPage.get(key);
    if (existing) {
      byPage.set(key, {
        ...existing,
        citedText: existing.citedText + " ... " + c.citedText,
        highlightAreas: [...existing.highlightAreas, ...c.highlightAreas],
      });
    } else {
      byPage.set(key, { ...c });
    }
  }
  return Array.from(byPage.values());
}

export function MessageBubble({ message, onCitationClick }: MessageBubbleProps) {
  const isUser = message.role === "user";
  const mergedCitations = useMemo(
    () => deduplicateCitations(message.citations),
    [message.citations]
  );

  return (
    <div className={cn("flex gap-2", isUser ? "justify-end" : "justify-start")}>
      {!isUser && (
        <div className="flex items-start pt-1">
          <div className="flex items-center justify-center h-6 w-6 rounded-full bg-primary/10 text-primary shrink-0">
            <Bot className="h-3.5 w-3.5" />
          </div>
        </div>
      )}
      <div
        className={cn(
          "max-w-[80%] rounded-xl px-3.5 py-2.5",
          isUser
            ? "bg-primary text-primary-foreground rounded-br-sm"
            : "bg-white border border-border/60 shadow-sm rounded-bl-sm"
        )}
      >
        {!isUser && message.isStreaming && !message.content ? (
          <div className="flex items-center gap-1.5 py-1">
            <div className="flex items-center gap-1">
              <span className="h-1.5 w-1.5 rounded-full bg-primary/60 animate-[bounce_1.4s_ease-in-out_infinite]" />
              <span className="h-1.5 w-1.5 rounded-full bg-primary/60 animate-[bounce_1.4s_ease-in-out_0.2s_infinite]" />
              <span className="h-1.5 w-1.5 rounded-full bg-primary/60 animate-[bounce_1.4s_ease-in-out_0.4s_infinite]" />
            </div>
            <span className="text-xs text-muted-foreground">Analyzing document...</span>
          </div>
        ) : (
          <p className="text-sm whitespace-pre-wrap leading-relaxed">
            {message.content}
            {message.isStreaming && (
              <span className="inline-block w-1.5 h-4 ml-0.5 bg-primary animate-pulse align-text-bottom rounded-sm" />
            )}
          </p>
        )}

        {mergedCitations.length > 0 && (
          <div className="mt-2.5 space-y-1.5 border-t border-border/40 pt-2">
            {mergedCitations.map((citation, i) => (
              <CitationChip
                key={`${citation.pageNumber}-${i}`}
                citation={citation}
                onClick={onCitationClick}
              />
            ))}
          </div>
        )}
      </div>
      {isUser && (
        <div className="flex items-start pt-1">
          <div className="flex items-center justify-center h-6 w-6 rounded-full bg-primary text-primary-foreground shrink-0">
            <User className="h-3.5 w-3.5" />
          </div>
        </div>
      )}
    </div>
  );
}
