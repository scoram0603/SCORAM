import { ImageIcon } from "lucide-react";
import { useSlotImage } from "../../hooks/useSlotImage";

// Every image on the landing page that isn't already a real project asset goes through here.
//
// With `slot` (a key of config/landingSlots.js) the image is admin-replaceable: the configured upload is tried
// first, then THAT slot's bundled default (never another slot's), then -- only if the default itself is missing --
// a clean labeled placeholder instead of the browser's broken-image icon. Each candidate is tried once, so a bad URL
// can't loop. `src` is the legacy/default path used when no slot is given (and as the placeholder's caption).
// The aspect-ratio class is applied to the <img> too, so space is reserved while it loads (no layout shift).
export default function AssetImage({ src, slot, alt, label, aspect = "aspect-[4/3]", className = "" }) {
  const resolved = useSlotImage(slot || "", { fallbackSrc: src });

  if (!resolved.exhausted) {
    return (
      <img
        key={resolved.src}
        src={resolved.src}
        alt={alt}
        loading="lazy"
        decoding="async"
        onError={resolved.onError}
        className={`${aspect} ${className}`}
      />
    );
  }

  return (
    <div
      className={`flex ${aspect} w-full flex-col items-center justify-center gap-2 rounded-xl2 border-2 border-dashed border-primary-100 bg-primary-50/60 p-6 text-center ${className}`}
    >
      <ImageIcon className="h-8 w-8 text-primary-400" strokeWidth={1.5} />
      <p className="text-xs font-semibold text-primary-400">{label || "Image required"}</p>
      {src && <p className="font-mono text-[11px] text-primary-400">{src}</p>}
    </div>
  );
}
