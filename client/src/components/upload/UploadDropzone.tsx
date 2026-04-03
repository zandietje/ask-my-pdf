import { useCallback, useState, useRef } from "react";
import { Upload, Loader2, CheckCircle2 } from "lucide-react";
import { cn } from "@/lib/utils";

interface UploadDropzoneProps {
  onUpload: (file: File) => Promise<void>;
}

export function UploadDropzone({ onUpload }: UploadDropzoneProps) {
  const [isDragging, setIsDragging] = useState(false);
  const [isUploading, setIsUploading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  const handleFile = useCallback(async (file: File) => {
    if (!file.name.toLowerCase().endsWith(".pdf")) {
      setError("Only PDF files are accepted");
      return;
    }
    if (file.size > 32 * 1024 * 1024) {
      setError("File exceeds 32MB limit");
      return;
    }
    setError(null);
    setSuccess(null);
    setIsUploading(true);
    try {
      await onUpload(file);
      setSuccess(`"${file.name}" uploaded`);
      setTimeout(() => setSuccess(null), 3000);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Upload failed");
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
    <div>
      <div
        onClick={handleClick}
        onDragOver={handleDragOver}
        onDragLeave={handleDragLeave}
        onDrop={handleDrop}
        className={cn(
          "flex flex-col items-center justify-center gap-1.5 rounded-lg border-2 border-dashed p-3 cursor-pointer transition-all",
          isDragging
            ? "border-primary bg-primary/5 scale-[1.02]"
            : "border-border hover:border-primary/40 hover:bg-accent/30",
          isUploading && "pointer-events-none opacity-60"
        )}
      >
        {isUploading ? (
          <Loader2 className="h-5 w-5 animate-spin text-primary" />
        ) : (
          <Upload className="h-5 w-5 text-muted-foreground" />
        )}
        <p className="text-xs text-muted-foreground">
          {isUploading ? "Processing PDF..." : "Drop a PDF here, or click to select"}
        </p>
        <input
          ref={inputRef}
          type="file"
          accept=".pdf"
          onChange={handleInputChange}
          className="hidden"
        />
      </div>
      {error && (
        <p className="mt-1.5 text-xs text-destructive">{error}</p>
      )}
      {success && (
        <p className="mt-1.5 text-xs text-emerald-600 flex items-center gap-1">
          <CheckCircle2 className="h-3 w-3" />
          {success}
        </p>
      )}
    </div>
  );
}
