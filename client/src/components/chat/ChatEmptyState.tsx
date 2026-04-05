import { MessageSquareText, FileSearch, ArrowRight } from "lucide-react";

const SUGGESTED_QUESTIONS = [
  "What is this document about?",
  "Summarize the key points",
  "What are the main conclusions?",
];

interface ChatEmptyStateProps {
  hasDocument: boolean;
  documentName: string | null;
  onSuggestion: (question: string) => void;
}

export function ChatEmptyState({ hasDocument, documentName, onSuggestion }: ChatEmptyStateProps) {
  if (!hasDocument) {
    return (
      <div className="flex flex-col items-center justify-center h-full text-center gap-4 p-6">
        <div className="h-14 w-14 rounded-2xl bg-secondary flex items-center justify-center">
          <FileSearch className="h-7 w-7 text-muted-foreground" />
        </div>
        <div className="space-y-1.5">
          <p className="text-base font-medium text-foreground">Welcome to Ask My PDF</p>
          <p className="text-sm text-muted-foreground max-w-xs">
            Upload a PDF document and ask any question about its contents
          </p>
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-col items-center justify-center h-full text-center gap-6 p-6">
      <div className="space-y-1.5">
        <p className="text-base font-medium text-foreground">Ready to answer</p>
        <p className="text-sm text-muted-foreground">
          Ask any question about <span className="font-medium text-foreground">{documentName ?? "the document"}</span>
        </p>
      </div>
      <div className="grid grid-cols-1 gap-2 w-full max-w-sm">
        {SUGGESTED_QUESTIONS.map((q) => (
          <button
            key={q}
            type="button"
            onClick={() => onSuggestion(q)}
            className="flex items-center gap-3 text-left rounded-xl border border-border
                       bg-card hover:bg-accent/50 px-4 py-3 transition-colors group"
          >
            <MessageSquareText className="h-4 w-4 text-muted-foreground shrink-0" />
            <span className="text-sm text-muted-foreground group-hover:text-foreground transition-colors flex-1">
              {q}
            </span>
            <ArrowRight className="h-3.5 w-3.5 text-muted-foreground/50 group-hover:text-foreground transition-all" />
          </button>
        ))}
      </div>
    </div>
  );
}
