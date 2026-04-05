import { motion } from "framer-motion";
import type { EngineInfo } from "@/lib/types";
import { cn } from "@/lib/utils";
import { Zap, Microscope } from "lucide-react";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui/tooltip";

interface EngineSelectorProps {
  engines: EngineInfo[];
  selected: string;
  onChange: (key: string) => void;
}

const engineMeta: Record<string, { icon: typeof Zap; label: string; description: string }> = {
  rag: {
    icon: Zap,
    label: "Quick",
    description: "Searches relevant passages via RAG",
  },
  "claude-cli": {
    icon: Microscope,
    label: "Deep",
    description: "Thorough exact analysis",
  },
};

export function EngineSelector({ engines, selected, onChange }: EngineSelectorProps) {
  if (engines.length <= 1) return null;

  return (
    <TooltipProvider delayDuration={300}>
      <div className="flex rounded-lg bg-secondary p-0.5 gap-0.5">
        {engines.map((engine) => {
          const meta = engineMeta[engine.key] ?? {
            icon: Zap,
            label: engine.name,
            description: "",
          };
          const Icon = meta.icon;
          const isSelected = engine.key === selected;

          return (
            <Tooltip key={engine.key}>
              <TooltipTrigger asChild>
                <button
                  type="button"
                  onClick={() => onChange(engine.key)}
                  className={cn(
                    "relative flex items-center justify-center rounded-md text-xs font-medium transition-colors duration-150",
                    "px-2.5 py-1.5 md:px-3 md:gap-1.5",
                    isSelected
                      ? "text-primary"
                      : "text-muted-foreground hover:text-foreground hover:bg-surface-hover"
                  )}
                >
                  {isSelected && (
                    <motion.div
                      layoutId="engine-indicator"
                      className="absolute inset-0 bg-card rounded-md shadow-sm ring-1 ring-border/50"
                      transition={{ type: "spring", stiffness: 400, damping: 30 }}
                    />
                  )}
                  <span className="relative z-10 flex items-center justify-center gap-1.5">
                    <Icon className="h-3.5 w-3.5 md:h-3 md:w-3 shrink-0" />
                    <span>{meta.label}</span>
                  </span>
                </button>
              </TooltipTrigger>
              {meta.description && (
                <TooltipContent>
                  <p>{meta.description}</p>
                </TooltipContent>
              )}
            </Tooltip>
          );
        })}
      </div>
    </TooltipProvider>
  );
}
