import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import type { Components } from "react-markdown";

interface MessageContentProps {
  content: string;
  isStreaming?: boolean;
}

const components: Components = {
  h1: ({ children }) => <h1 className="text-base font-semibold mt-4 mb-2">{children}</h1>,
  h2: ({ children }) => <h2 className="text-sm font-semibold mt-3 mb-1.5">{children}</h2>,
  h3: ({ children }) => <h3 className="text-sm font-medium mt-2 mb-1">{children}</h3>,
  p: ({ children }) => <p className="text-sm leading-relaxed mb-2 last:mb-0">{children}</p>,
  ul: ({ children }) => <ul className="list-disc pl-4 mb-2 space-y-0.5 text-sm">{children}</ul>,
  ol: ({ children }) => <ol className="list-decimal pl-4 mb-2 space-y-0.5 text-sm">{children}</ol>,
  li: ({ children }) => <li className="text-sm leading-relaxed">{children}</li>,
  strong: ({ children }) => <strong className="font-semibold">{children}</strong>,
  em: ({ children }) => <em className="italic">{children}</em>,
  code: ({ className, children }) => {
    const isBlock = className?.includes("language-");
    if (isBlock) {
      return <code className="text-xs font-mono block">{children}</code>;
    }
    return (
      <code className="bg-secondary px-1.5 py-0.5 rounded text-xs font-mono">{children}</code>
    );
  },
  pre: ({ children }) => (
    <pre className="bg-secondary rounded-lg p-3 overflow-x-auto mb-2">{children}</pre>
  ),
  table: ({ children }) => (
    <table className="w-full text-xs border-collapse mb-2">{children}</table>
  ),
  th: ({ children }) => (
    <th className="border border-border bg-secondary px-2 py-1 text-left font-medium">{children}</th>
  ),
  td: ({ children }) => (
    <td className="border border-border px-2 py-1">{children}</td>
  ),
  blockquote: ({ children }) => (
    <blockquote className="border-l-2 border-primary/30 pl-3 italic text-muted-foreground mb-2">{children}</blockquote>
  ),
  a: ({ href, children }) => (
    <a href={href} className="text-primary underline underline-offset-2" target="_blank" rel="noopener noreferrer">{children}</a>
  ),
  hr: () => <hr className="border-border my-3" />,
};

export function MessageContent({ content, isStreaming }: MessageContentProps) {
  return (
    <div className="min-w-0">
      <ReactMarkdown remarkPlugins={[remarkGfm]} components={components}>
        {content}
      </ReactMarkdown>
      {isStreaming && (
        <span className="inline-block w-1.5 h-4 ml-0.5 bg-primary animate-pulse align-text-bottom rounded-sm" />
      )}
    </div>
  );
}
