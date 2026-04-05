import { useMemo } from "react";
import type { ChatMessage, Citation } from "@/lib/types";
import { CitationChip } from "./CitationChip";
import { Bot } from "lucide-react";

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

/** Strip inline [C<n>] chunk-ID references — citations are shown as evidence below. */
function stripChunkReferences(content: string): string {
  return content.replace(/\s*\[C\d+\]/g, "");
}

export function MessageBubble({ message, onCitationClick }: MessageBubbleProps) {
  const isUser = message.role === "user";
  const mergedCitations = useMemo(
    () => deduplicateCitations(message.citations),
    [message.citations]
  );

  // Strip [C<n>] references after streaming completes — citations shown as evidence below
  const displayContent = useMemo(
    () => message.isStreaming ? message.content : stripChunkReferences(message.content),
    [message.content, message.isStreaming]
  );

  if (isUser) {
    return (
      <div className="flex justify-end">
        <div className="max-w-[92%] md:max-w-[85%] rounded-2xl rounded-br-sm bg-primary text-primary-foreground px-4 py-3 md:px-3.5 md:py-2.5">
          <p className="text-base md:text-sm whitespace-pre-wrap leading-relaxed">{message.content}</p>
        </div>
      </div>
    );
  }

  return (
    <div className="flex gap-2.5 justify-start">
      <div className="flex items-start pt-0.5 shrink-0">
        <div className="flex items-center justify-center h-8 w-8 md:h-7 md:w-7 rounded-full bg-primary/10 text-primary">
          <Bot className="h-4.5 w-4.5 md:h-4 md:w-4" />
        </div>
      </div>
      <div className="max-w-[95%] md:max-w-[90%] min-w-0">
        <div className="rounded-2xl rounded-tl-sm bg-white border border-border/60 shadow-sm px-4 py-3 md:px-3.5 md:py-2.5">
          {message.isStreaming && !message.content ? (
            <div className="flex items-center gap-1.5 py-1">
              <div className="flex items-center gap-1">
                <span className="h-2 w-2 md:h-1.5 md:w-1.5 rounded-full bg-primary/60 animate-[bounce_1.4s_ease-in-out_infinite]" />
                <span className="h-2 w-2 md:h-1.5 md:w-1.5 rounded-full bg-primary/60 animate-[bounce_1.4s_ease-in-out_0.2s_infinite]" />
                <span className="h-2 w-2 md:h-1.5 md:w-1.5 rounded-full bg-primary/60 animate-[bounce_1.4s_ease-in-out_0.4s_infinite]" />
              </div>
              <span className="text-sm md:text-xs text-muted-foreground">Analyzing document...</span>
            </div>
          ) : (
            <p className="text-base md:text-sm whitespace-pre-wrap leading-relaxed">
              {displayContent}
              {message.isStreaming && (
                <span className="inline-block w-1.5 h-4 ml-0.5 bg-primary animate-pulse align-text-bottom rounded-sm" />
              )}
            </p>
          )}
        </div>

        {mergedCitations.length > 0 && (
          <div className="mt-2.5 md:mt-2 space-y-2 md:space-y-1.5">
            <span className="text-xs md:text-[11px] font-medium text-muted-foreground uppercase tracking-wider">
              Evidence
            </span>
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
    </div>
  );
}
