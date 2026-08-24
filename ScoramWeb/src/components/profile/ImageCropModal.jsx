import { useEffect, useMemo, useRef, useState } from "react";
import { Check, Loader2, X, ZoomIn } from "lucide-react";

const PREVIEW_SIZE = 288; // CSS px -- the square crop viewport shown to the person
const OUTPUT_SIZE = 512; // px -- the square JPEG actually uploaded
const MIN_ZOOM = 1;
const MAX_ZOOM = 3;

// A small, dependency-free square-crop dialog: drag the image to reposition it, use the slider to
// zoom, then Save renders exactly what's inside the viewport onto an offscreen canvas and hands
// the caller a cropped JPEG Blob. No cropping library -- the math is simple enough (see
// clampOffset/handleSave below) that pulling one in wasn't worth the bundle weight for one screen.
//
// file: the raw File the person picked (from an <input type="file">).
// onCancel: called with no changes made.
// onCropped(file): called with a File (not a bare Blob -- see ProfilePhoto's comment on why) once
// the person hits Save.
export default function ImageCropModal({ file, onCancel, onCropped }) {
  const imgRef = useRef(null);
  const containerRef = useRef(null);
  const dragState = useRef(null); // { startX, startY, startOffsetX, startOffsetY } while dragging

  const [imageUrl, setImageUrl] = useState(null);
  const [naturalSize, setNaturalSize] = useState(null); // { width, height }
  const [zoom, setZoom] = useState(MIN_ZOOM);
  const [offset, setOffset] = useState({ x: 0, y: 0 }); // top-left of the image, in container px
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    const url = URL.createObjectURL(file);
    setImageUrl(url);
    return () => URL.revokeObjectURL(url);
  }, [file]);

  // baseScale: the zoom level at which the image's SHORTER side exactly fills the viewport (i.e.
  // "cover" behaviour) -- this is what zoom = 1 means. Below this the image would leave gaps.
  const baseScale = useMemo(() => {
    if (!naturalSize) return 1;
    return PREVIEW_SIZE / Math.min(naturalSize.width, naturalSize.height);
  }, [naturalSize]);

  const effectiveScale = baseScale * zoom;
  const displayedWidth = naturalSize ? naturalSize.width * effectiveScale : 0;
  const displayedHeight = naturalSize ? naturalSize.height * effectiveScale : 0;

  function clampOffset(next, scale = effectiveScale) {
    if (!naturalSize) return next;
    const w = naturalSize.width * scale;
    const h = naturalSize.height * scale;
    // The image must always fully cover the viewport: its top-left can range from
    // (viewport - imageSize) up to 0 on each axis.
    const minX = Math.min(0, PREVIEW_SIZE - w);
    const minY = Math.min(0, PREVIEW_SIZE - h);
    return { x: Math.min(0, Math.max(minX, next.x)), y: Math.min(0, Math.max(minY, next.y)) };
  }

  function handleImageLoad(e) {
    const { naturalWidth, naturalHeight } = e.target;
    setNaturalSize({ width: naturalWidth, height: naturalHeight });
    // Center the image in the viewport at zoom = 1.
    const scale = PREVIEW_SIZE / Math.min(naturalWidth, naturalHeight);
    setOffset({
      x: (PREVIEW_SIZE - naturalWidth * scale) / 2,
      y: (PREVIEW_SIZE - naturalHeight * scale) / 2,
    });
  }

  function handleZoomChange(nextZoom) {
    const nextScale = baseScale * nextZoom;
    setZoom(nextZoom);
    setOffset((prev) => clampOffset(prev, nextScale));
  }

  function handlePointerDown(e) {
    e.currentTarget.setPointerCapture(e.pointerId);
    dragState.current = { startX: e.clientX, startY: e.clientY, startOffsetX: offset.x, startOffsetY: offset.y };
  }

  function handlePointerMove(e) {
    if (!dragState.current) return;
    const dx = e.clientX - dragState.current.startX;
    const dy = e.clientY - dragState.current.startY;
    setOffset(clampOffset({ x: dragState.current.startOffsetX + dx, y: dragState.current.startOffsetY + dy }));
  }

  function handlePointerUp() {
    dragState.current = null;
  }

  async function handleSave() {
    if (!naturalSize) return;
    setSaving(true);
    try {
      // Map the viewport (always (0,0)-(PREVIEW_SIZE,PREVIEW_SIZE) in container coordinates) back
      // to natural-image pixel coordinates, given where the image currently sits underneath it.
      const sx = -offset.x / effectiveScale;
      const sy = -offset.y / effectiveScale;
      const sSize = PREVIEW_SIZE / effectiveScale;

      const canvas = document.createElement("canvas");
      canvas.width = OUTPUT_SIZE;
      canvas.height = OUTPUT_SIZE;
      const ctx = canvas.getContext("2d");
      ctx.drawImage(imgRef.current, sx, sy, sSize, sSize, 0, 0, OUTPUT_SIZE, OUTPUT_SIZE);

      const blob = await new Promise((resolve) => canvas.toBlob(resolve, "image/jpeg", 0.92));
      const croppedFile = new File([blob], "profile-photo.jpg", { type: "image/jpeg" });
      onCropped(croppedFile);
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-ink-900/60 p-4">
      <div className="w-full max-w-sm rounded-2xl bg-white p-5 shadow-xl">
        <div className="flex items-center justify-between">
          <h2 className="text-[15px] font-bold text-ink-900">Adjust your photo</h2>
          <button type="button" onClick={onCancel} className="text-ink-400 hover:text-ink-600">
            <X className="h-5 w-5" strokeWidth={2} />
          </button>
        </div>

        <div
          ref={containerRef}
          className="relative mx-auto mt-4 touch-none overflow-hidden rounded-full border border-primary-100 bg-ink-900/5"
          style={{ width: PREVIEW_SIZE, height: PREVIEW_SIZE, cursor: "grab" }}
          onPointerDown={handlePointerDown}
          onPointerMove={handlePointerMove}
          onPointerUp={handlePointerUp}
          onPointerLeave={handlePointerUp}
        >
          {imageUrl && (
            <img
              ref={imgRef}
              src={imageUrl}
              alt="Crop preview"
              onLoad={handleImageLoad}
              draggable={false}
              className="absolute select-none"
              style={{ left: offset.x, top: offset.y, width: displayedWidth, height: displayedHeight }}
            />
          )}
        </div>

        <p className="mt-3 text-center text-xs text-ink-400">Drag to reposition</p>

        <div className="mt-3 flex items-center gap-3">
          <ZoomIn className="h-4 w-4 shrink-0 text-ink-400" strokeWidth={2} />
          <input
            type="range"
            min={MIN_ZOOM}
            max={MAX_ZOOM}
            step={0.01}
            value={zoom}
            onChange={(e) => handleZoomChange(Number(e.target.value))}
            className="w-full accent-primary-600"
          />
        </div>

        <div className="mt-5 flex gap-2">
          <button
            type="button"
            onClick={onCancel}
            disabled={saving}
            className="flex flex-1 items-center justify-center rounded-xl2 border border-primary-100 px-4 py-2.5 text-sm font-semibold text-ink-600 disabled:opacity-60"
          >
            Cancel
          </button>
          <button
            type="button"
            onClick={handleSave}
            disabled={saving || !naturalSize}
            className="flex flex-1 items-center justify-center gap-1.5 rounded-xl2 bg-primary-600 px-4 py-2.5 text-sm font-semibold text-white transition-colors hover:bg-primary-700 disabled:opacity-60"
          >
            {saving ? <Loader2 className="h-4 w-4 animate-spin" strokeWidth={2.5} /> : <Check className="h-4 w-4" strokeWidth={2.5} />}
            Save
          </button>
        </div>
      </div>
    </div>
  );
}
