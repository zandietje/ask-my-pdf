import { AnimatePresence, motion } from "framer-motion";
import type { ReactNode } from "react";

interface AnimatedListItemProps {
  children: ReactNode;
  id: string;
}

export function AnimatedListItem({ children, id }: AnimatedListItemProps) {
  return (
    <motion.div
      key={id}
      initial={{ opacity: 0, y: 8 }}
      animate={{ opacity: 1, y: 0 }}
      exit={{ opacity: 0, scale: 0.95 }}
      transition={{ duration: 0.2, ease: "easeOut" }}
      layout
    >
      {children}
    </motion.div>
  );
}

interface AnimatedListProps {
  children: ReactNode;
}

export function AnimatedList({ children }: AnimatedListProps) {
  return <AnimatePresence initial={false}>{children}</AnimatePresence>;
}
