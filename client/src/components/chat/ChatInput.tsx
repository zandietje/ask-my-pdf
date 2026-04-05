import { useRef, useEffect } from "react";
import { motion } from "framer-motion";
import { Send, FileText } from "lucide-react";

interface ChatInputProps {
  value: string;
  onChange: (value: string) => void;
  onSend: () => void;
  disabled: boolean;
  placeholder: string;
  documentName: string | null;
  hasDocument: boolean;
}

export function ChatInput({ value, onChange, onSend, disabled, placeholder, documentName, hasDocument }: ChatInputProps) {
  const textareaRef = useRef<HTMLTextAreaElement>(null);

  useEffect(() => {
    const el = textareaRef.current;
    if (!el) return;
    el.style.height = "auto";
    el.style.height = Math.min(el.scrollHeight, 128) + "px";
  }, [value]);

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      if (!disabled && value.trim()) {
        onSend();
      }
    }
  };

  return (
    <div className="border-t bg-card shadow-[0_-1px_3px_rgba(0,0,0,0.04)] safe-area-bottom">
      {hasDocument && documentName && (
        <div className="px-4 pt-2.5 flex items-center gap-1.5 text-xs md:text-[11px] text-muted-foreground">
          <FileText className="h-3.5 w-3.5 md:h-3 md:w-3 shrink-0" />
          <span className="truncate">Asking about <span className="font-medium text-foreground">{documentName}</span></span>
        </div>
      )}
      <div className="p-4 pt-2 md:p-3 md:pt-2">
        <div className="bg-card border border-border rounded-xl shadow-sm focus-within:border-primary/50 focus-within:ring-1 focus-within:ring-primary/20 transition-all">
          <div className="flex items-end gap-2 p-2">
            <textarea
              ref={textareaRef}
              value={value}
              onChange={(e) => onChange(e.target.value)}
              onKeyDown={handleKeyDown}
              placeholder={placeholder}
              disabled={disabled}
              rows={1}
              inputMode="text"
              className="flex-1 resize-none bg-transparent border-0 focus:ring-0 focus:outline-none text-sm placeholder:text-muted-foreground px-2 py-1.5 max-h-32 disabled:opacity-50"
            />
            <motion.button
              type="button"
              onClick={onSend}
              disabled={!value.trim() || disabled}
              whileTap={{ scale: 0.92 }}
              className="shrink-0 rounded-lg h-9 w-9 bg-primary text-primary-foreground flex items-center justify-center disabled:opacity-50 disabled:pointer-events-none"
            >
              <Send className="h-4 w-4" />
            </motion.button>
          </div>
        </div>
        <p className="hidden md:block text-[11px] text-muted-foreground mt-1.5 px-1">
          Enter to send · Shift+Enter for new line
        </p>
      </div>
    </div>
  );
}
