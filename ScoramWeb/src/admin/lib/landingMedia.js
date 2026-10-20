// Client-side checks for a landing-page image BEFORE upload (instant feedback only -- the server re-validates the
// extension, size and file content, and is the real gate).
export const DEFAULT_MAX_BYTES = 3 * 1024 * 1024;
export const DEFAULT_EXTENSIONS = [".png", ".jpg", ".jpeg", ".webp"];

export function formatBytes(bytes) {
  if (bytes >= 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  return `${Math.max(1, Math.round(bytes / 1024))} KB`;
}

/** -> error message string, or null when the file looks acceptable. */
export function validateImageFile(file, { maxBytes = DEFAULT_MAX_BYTES, extensions = DEFAULT_EXTENSIONS } = {}) {
  if (!file) return "Choose an image file first.";
  const name = String(file.name || "").toLowerCase();
  const ext = name.includes(".") ? name.slice(name.lastIndexOf(".")) : "";
  if (!extensions.includes(ext)) return `Only ${extensions.map((e) => e.slice(1).toUpperCase()).join(", ")} images are allowed.`;
  if (file.size === 0) return "That file is empty.";
  if (file.size > maxBytes) return `The image is ${formatBytes(file.size)}. The maximum is ${formatBytes(maxBytes)}.`;
  return null;
}
