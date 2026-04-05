import { useCallback, useState, useRef } from "react";
import { Upload, Loader2 } from "lucide-react";
import { toast } from "sonner";
import { cn } from "@/lib/utils";

interface UploadDropzoneProps {
  onUpload: (file: File) => Promise<void>;
}

export function UploadDropzone({ onUpload }: UploadDropzoneProps) {
  const [isDragging, setIsDragging] = useState(false);
  const [isUploading, setIsUploading] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  const handleFile = useCallback(async (file: File) => {
    if (!file.name.toLowerCase().endsWith(".pdf")) {
      toast.error("Only PDF files are accepted");
      return;
    }
    if (file.size > 32 * 1024 * 1024) {
      toast.error("File exceeds the 32 MB size limit");
      return;
    }
    setIsUploading(true);
    try {
      await onUpload(file);
      toast.success(`"${file.name}" uploaded`);
    } catch (e) {
      toast.error(e instanceof Error ? e.message : "Upload failed. Please try again.");
    } finally {
      setIsUploading(false);
    }
  }, [onUpload]);

  const handleDragOver = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setIsDragging(true);
  }, []);

  const handleDragLeave = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setIsDragging(false);
  }, []);

  const handleDrop = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setIsDragging(false);
    const file = e.dataTransfer.files[0];
    if (file) handleFile(file);
  }, [handleFile]);

  const handleClick = useCallback(() => {
    inputRef.current?.click();
  }, []);

  const handleInputChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (file) handleFile(file);
    e.target.value = "";
  }, [handleFile]);

  return (
    <div
      onClick={handleClick}
      onDragOver={handleDragOver}
      onDragLeave={handleDragLeave}
      onDrop={handleDrop}
      className={cn(
        "flex items-center gap-3 rounded-xl border-2 border-dashed p-4 md:p-3.5 cursor-pointer transition-all",
        isDragging
          ? "border-primary bg-primary/5 scale-[1.01]"
          : "border-border hover:border-primary/40 hover:bg-accent/30",
        isUploading && "pointer-events-none opacity-60"
      )}
    >
      <div className={cn(
        "flex items-center justify-center h-10 w-10 md:h-9 md:w-9 rounded-lg shrink-0 transition-colors",
        isDragging ? "bg-primary/10 text-primary" : "bg-muted text-muted-foreground"
      )}>
        {isUploading ? (
          <Loader2 className="h-5 w-5 animate-spin text-primary" />
        ) : (
          <Upload className="h-5 w-5" />
        )}
      </div>
      <div className="min-w-0">
        <p className="text-base md:text-sm font-medium text-foreground">
          {isUploading ? "Processing..." : "Upload PDF"}
        </p>
        <p className="text-xs md:text-[11px] text-muted-foreground">
          {isUploading ? "Extracting text and bounding boxes" : "Drag & drop or click to browse"}
        </p>
      </div>
      <input
        ref={inputRef}
        type="file"
        accept=".pdf"
        onChange={handleInputChange}
        className="hidden"
      />
    </div>
  );
}
