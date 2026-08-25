import { useState } from "react";
import { ImageIcon } from "lucide-react";

// Every image on the landing page that isn't already a real project asset (logo, exam logos from
// the API) goes through here. Until Durgesh drops the real file at `src` (see LANDING_REPORT.md for
// the exact path + spec for each one), this renders a clean labeled placeholder instead of a
// browser's broken-image icon -- so the page still looks intentional before the assets exist.
export default function AssetImage({ src, alt, label, aspect = "aspect-[4/3]", className = "" }) {
  const [failed, setFailed] = useState(false);

  if (!failed) {
    return (
      <img
        src={src}
        alt={alt}
        loading="lazy"
        onError={() => setFailed(true)}
        className={className}
      />
    );
  }

  return (
    <div
      className={`flex ${aspect} w-full flex-col items-center justify-center gap-2 rounded-xl2 border-2 border-dashed border-primary-100 bg-primary-50/60 p-6 text-center ${className}`}
    >
      <ImageIcon className="h-8 w-8 text-primary-400" strokeWidth={1.5} />
      <p className="text-xs font-semibold text-primary-400">{label || "Image required"}</p>
      <p className="font-mono text-[11px] text-primary-400">{src}</p>
    </div>
  );
}
