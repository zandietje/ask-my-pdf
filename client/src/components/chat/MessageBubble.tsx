import { useMemo } from "react";
import { motion, AnimatePresence } from "framer-motion";
import type { ChatMessage, Citation } from "@/lib/types";
import { CitationChip } from "./CitationChip";
import { MessageContent } from "./MessageContent";
import { StreamingIndicator } from "./StreamingIndicator";
import { Bot, AlertCircle } from "lucide-react";

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

  const displayContent = useMemo(
    () => stripChunkReferences(message.content),
    [message.content]
  );

  const isError = !isUser && (
    message.content.startsWith("Error:") ||
    message.content === "An error occurred. Please try again."
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
        <div className="h-7 w-7 rounded-full bg-primary/10 text-primary flex items-center justify-center">
          <Bot className="h-4 w-4" />
        </div>
      </div>
      <div className="max-w-[95%] min-w-0 space-y-2">
        <div className="rounded-2xl rounded-tl-sm bg-card border border-border shadow-sm px-4 py-3">
          {message.isStreaming && !message.content ? (
            <StreamingIndicator />
          ) : isError ? (
            <div className="flex items-center gap-2 text-destructive text-sm">
              <AlertCircle className="h-4 w-4 shrink-0" />
              <span>{message.content}</span>
            </div>
          ) : (
            <MessageContent content={displayContent} isStreaming={message.isStreaming} />
          )}
        </div>

        {mergedCitations.length > 0 && (
          <div className="space-y-1.5">
            <span className="text-[11px] font-medium text-muted-foreground uppercase tracking-wider">
              Sources
            </span>
            <motion.div
              className="flex flex-wrap gap-1.5"
              initial="hidden"
              animate="visible"
              variants={{
                hidden: {},
                visible: { transition: { staggerChildren: 0.05 } },
              }}
            >
              <AnimatePresence>
                {mergedCitations.map((citation, i) => (
                  <motion.div
                    key={`${citation.pageNumber}-${i}`}
                    variants={{
                      hidden: { opacity: 0, x: -8 },
                      visible: { opacity: 1, x: 0 },
                    }}
                    transition={{ duration: 0.15, ease: "easeOut" }}
                  >
                    <CitationChip citation={citation} onClick={onCitationClick} />
                  </motion.div>
                ))}
              </AnimatePresence>
            </motion.div>
          </div>
        )}
      </div>
    </div>
  );
}
