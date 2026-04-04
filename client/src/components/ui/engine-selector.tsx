import type { EngineInfo } from "@/lib/types";
import { cn } from "@/lib/utils";
import { Zap, Search } from "lucide-react";

interface EngineSelectorProps {
  engines: EngineInfo[];
  selected: string;
  onChange: (key: string) => void;
}

const engineMeta: Record<string, { icon: typeof Zap; label: string; description: string }> = {
  anthropic: {
    icon: Zap,
    label: "Quick",
    description: "Fast streaming responses",
  },
  "claude-cli": {
    icon: Search,
    label: "Deep",
    description: "Thorough analysis",
  },
};

export function EngineSelector({ engines, selected, onChange }: EngineSelectorProps) {
  if (engines.length <= 1) return null;

  return (
    <div className="space-y-1">
      <label className="text-[11px] font-medium text-muted-foreground uppercase tracking-wider">
        Analysis Mode
      </label>
      <div className="flex rounded-lg bg-slate-100 p-0.5 gap-0.5">
        {engines.map((engine) => {
          const meta = engineMeta[engine.key] ?? {
            icon: Zap,
            label: engine.name,
            description: "",
          };
          const Icon = meta.icon;
          const isSelected = engine.key === selected;

          return (
            <button
              key={engine.key}
              type="button"
              onClick={() => onChange(engine.key)}
              className={cn(
                "flex-1 flex items-center justify-center gap-1.5 rounded-md px-2.5 py-1.5 text-xs font-medium transition-all duration-150",
                isSelected
                  ? "bg-white text-primary shadow-sm ring-1 ring-border/50"
                  : "text-muted-foreground hover:text-foreground hover:bg-slate-50"
              )}
            >
              <Icon className="h-3 w-3 shrink-0" />
              <span>{meta.label}</span>
              {meta.description && (
                <span className={cn(
                  "text-[10px] font-normal",
                  isSelected ? "text-muted-foreground" : "text-muted-foreground/60"
                )}>
                  · {meta.description}
                </span>
              )}
            </button>
          );
        })}
      </div>
    </div>
  );
}
