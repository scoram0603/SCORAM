import { useEffect, useMemo, useRef, useState } from "react";
import { ImageIcon, Loader2, UploadCloud, X, RotateCcw } from "lucide-react";
import { useAdminAuth } from "../context/AdminAuthContext";
import {
  getLandingContentAdmin, uploadLandingMedia, resetLandingMedia, saveSmartProgress, resetSmartProgress,
} from "../api/landingContent";
import { PageHeader, Card, Button, Alert, FormField, TextInput, TextArea, friendlyError } from "../components/AdminUI";
import ConfirmDialog from "../components/ConfirmDialog";
import { LANDING_SLOTS, toAbsoluteMediaUrl } from "../../config/landingSlots";
import { DEFAULT_SMART_PROGRESS, isSafeLink } from "../../data/smartProgressDefaults";
import { validateImageFile, formatBytes } from "../lib/landingMedia";

// Admin > Landing Page. Two things live here:
//   1. Images (logo, feature images, founder photos, Smart Progress image). Each slot shows what the site currently
//      uses -- the custom upload if one is set, otherwise the website's bundled default. A replacement is previewed
//      locally and only sent on "Save"; the server stores it before switching, so a failed upload keeps the old image.
//      "Use default" removes the custom upload so the bundled default shows again.
//   2. The Smart Progress section copy (heading, description, highlights, button, visibility).

function SlotCard({ slot, limits, token, onChanged }) {
  const inputRef = useRef(null);
  const [file, setFile] = useState(null);
  const [localUrl, setLocalUrl] = useState(null);
  const [error, setError] = useState(null);
  const [success, setSuccess] = useState(null);
  const [progress, setProgress] = useState(null); // null | 0..100
  const [resetOpen, setResetOpen] = useState(false);
  const [previewBroken, setPreviewBroken] = useState(false);
  const abortRef = useRef(null);

  const uploading = progress !== null;
  const customAbs = slot.customUrl ? toAbsoluteMediaUrl(slot.customUrl) : null;
  const defaultSrc = LANDING_SLOTS[slot.key]?.default ?? null;
  // What the website will actually show right now (custom first, then default) -- or the local pick while editing.
  const currentSrc = !previewBroken && customAbs ? customAbs : defaultSrc;

  useEffect(() => () => { if (localUrl) URL.revokeObjectURL(localUrl); }, [localUrl]);
  useEffect(() => { setPreviewBroken(false); }, [slot.customUrl]);

  function pick(e) {
    const chosen = e.target.files?.[0];
    e.target.value = "";
    setSuccess(null);
    if (!chosen) return;
    const problem = validateImageFile(chosen, { maxBytes: limits?.maxImageBytes, extensions: limits?.allowedExtensions });
    if (problem) { setError(problem); return; }
    setError(null);
    setFile(chosen);
    setLocalUrl(URL.createObjectURL(chosen));
  }

  function cancel() {
    abortRef.current?.abort();
    setFile(null);
    setLocalUrl(null);
    setError(null);
  }

  async function save() {
    if (!file || uploading) return;
    setError(null);
    setProgress(0);
    const controller = new AbortController();
    abortRef.current = controller;
    try {
      await uploadLandingMedia(token, slot.key, file, { onProgress: setProgress, signal: controller.signal });
      setFile(null);
      setLocalUrl(null);
      setSuccess("Image updated. The website shows it within about a minute.");
      await onChanged();
    } catch (err) {
      if (err.name !== "AbortError") setError(err.message || "The upload failed, so the current image was kept.");
    } finally {
      setProgress(null);
      abortRef.current = null;
    }
  }

  async function doReset() {
    await resetLandingMedia(token, slot.key);
    setResetOpen(false);
    setSuccess("Back to the default image.");
    await onChanged();
  }

  const shown = localUrl || currentSrc;

  return (
    <Card className="flex flex-col">
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <h3 className="text-sm font-extrabold text-ink-900">{slot.label}</h3>
          <p className="mt-0.5 text-xs text-ink-400">{slot.description}</p>
        </div>
        <span className={`shrink-0 rounded-full px-2.5 py-1 text-xs font-semibold ${slot.customUrl ? "bg-secondary-50 text-secondary-600" : "bg-ink-400/10 text-ink-600"}`}>
          {slot.customUrl ? "Custom" : defaultSrc ? "Default" : "None"}
        </span>
      </div>

      <div className="mt-3 flex h-44 items-center justify-center overflow-hidden rounded-xl border border-primary-100 bg-surface">
        {shown ? (
          <img
            key={shown}
            src={shown}
            alt={`${slot.label} preview`}
            onError={() => { if (!localUrl) setPreviewBroken(true); }}
            className="max-h-full max-w-full object-contain"
          />
        ) : (
          <span className="flex flex-col items-center gap-1 px-4 text-center text-xs text-ink-400">
            <ImageIcon className="h-6 w-6" strokeWidth={1.5} />
            No image set — the section works without one
          </span>
        )}
      </div>
      {localUrl && <p className="mt-1 text-xs font-semibold text-accent-600">New image selected (not saved yet): {file.name} · {formatBytes(file.size)}</p>}
      <p className="mt-2 text-xs text-ink-400">Recommended: {slot.recommended}. PNG, JPG or WebP, up to {formatBytes(limits?.maxImageBytes ?? 3 * 1024 * 1024)}.</p>

      {uploading && (
        <div className="mt-3" role="progressbar" aria-valuenow={progress} aria-valuemin={0} aria-valuemax={100} aria-label={`Uploading ${slot.label}`}>
          <div className="h-2 overflow-hidden rounded-full bg-primary-50"><div className="h-full bg-primary-600 transition-all" style={{ width: `${progress}%` }} /></div>
          <p className="mt-1 text-xs text-ink-400">{progress < 100 ? `Uploading… ${progress}%` : "Processing…"}</p>
        </div>
      )}
      {error && <div className="mt-3"><Alert type="error">{error}</Alert></div>}
      {success && <div className="mt-3"><Alert type="success">{success}</Alert></div>}

      <div className="mt-4 flex flex-wrap gap-2">
        <input ref={inputRef} type="file" accept=".png,.jpg,.jpeg,.webp,image/png,image/jpeg,image/webp" className="sr-only" onChange={pick} aria-label={`Choose a new image for ${slot.label}`} />
        {!file ? (
          <>
            <Button variant="secondary" className="px-3 py-1.5 text-xs" onClick={() => inputRef.current?.click()}>
              <UploadCloud className="h-3.5 w-3.5" strokeWidth={2.5} /> Replace image
            </Button>
            {slot.customUrl && (
              <Button variant="ghost" className="px-3 py-1.5 text-xs" onClick={() => setResetOpen(true)}>
                <RotateCcw className="h-3.5 w-3.5" strokeWidth={2.5} /> {defaultSrc ? "Use default" : "Remove image"}
              </Button>
            )}
          </>
        ) : (
          <>
            <Button className="px-3 py-1.5 text-xs" isLoading={uploading} onClick={save}>Save image</Button>
            <Button variant="ghost" className="px-3 py-1.5 text-xs" onClick={cancel}><X className="h-3.5 w-3.5" strokeWidth={2.5} /> Cancel</Button>
          </>
        )}
      </div>

      <ConfirmDialog
        open={resetOpen}
        title={defaultSrc ? "Use the default image?" : "Remove this image?"}
        message={defaultSrc ? "The uploaded image is removed and the website's built-in default is shown again." : "The uploaded image is removed."}
        confirmLabel={defaultSrc ? "Use default" : "Remove"}
        onConfirm={doReset}
        onCancel={() => setResetOpen(false)}
      />
    </Card>
  );
}

function SmartProgressEditor({ initial, limits, token, onSaved }) {
  const base = initial || DEFAULT_SMART_PROGRESS;
  const [published, setPublished] = useState(base.published !== false);
  const [heading, setHeading] = useState(base.heading);
  const [description, setDescription] = useState(base.description);
  const [highlights, setHighlights] = useState((base.highlights || []).join("\n"));
  const [ctaText, setCtaText] = useState(base.ctaText || "");
  const [ctaLink, setCtaLink] = useState(base.ctaLink || "");
  const [busy, setBusy] = useState(false);
  const [resetOpen, setResetOpen] = useState(false);
  const [notice, setNotice] = useState(null);

  const list = useMemo(() => highlights.split("\n").map((h) => h.trim()).filter(Boolean), [highlights]);
  const max = { heading: limits?.heading ?? 120, description: limits?.description ?? 500, highlights: limits?.highlights ?? 6, highlight: limits?.highlight ?? 120, ctaText: limits?.ctaText ?? 40 };

  function problem() {
    if (!heading.trim()) return "The heading can't be empty.";
    if (heading.trim().length > max.heading) return `The heading can be at most ${max.heading} characters.`;
    if (!description.trim()) return "The description can't be empty.";
    if (description.trim().length > max.description) return `The description can be at most ${max.description} characters.`;
    if (list.length > max.highlights) return `Use at most ${max.highlights} highlights (one per line).`;
    if (list.some((h) => h.length > max.highlight)) return `Each highlight can be at most ${max.highlight} characters.`;
    if (ctaText.trim().length > max.ctaText) return `The button text can be at most ${max.ctaText} characters.`;
    if (!!ctaText.trim() !== !!ctaLink.trim()) return "Provide both the button text and the button link, or leave both empty.";
    if (ctaLink.trim() && !isSafeLink(ctaLink.trim())) return "The button link must be a path on this site (e.g. /login?mode=register) or an https:// address.";
    return null;
  }

  async function save() {
    const p = problem();
    if (p) { setNotice({ type: "error", message: p }); return; }
    setBusy(true);
    setNotice(null);
    try {
      await saveSmartProgress(token, { published, heading: heading.trim(), description: description.trim(), highlights: list, ctaText: ctaText.trim(), ctaLink: ctaLink.trim() });
      setNotice({ type: "success", message: "Smart Progress section saved. The website shows it within about a minute." });
      await onSaved();
    } catch (err) {
      setNotice({ type: "error", message: friendlyError(err) });
    } finally {
      setBusy(false);
    }
  }

  async function doReset() {
    await resetSmartProgress(token);
    setResetOpen(false);
    setPublished(true); setHeading(DEFAULT_SMART_PROGRESS.heading); setDescription(DEFAULT_SMART_PROGRESS.description);
    setHighlights(DEFAULT_SMART_PROGRESS.highlights.join("\n")); setCtaText(DEFAULT_SMART_PROGRESS.ctaText); setCtaLink(DEFAULT_SMART_PROGRESS.ctaLink);
    setNotice({ type: "success", message: "Back to the default Smart Progress copy." });
    await onSaved();
  }

  return (
    <Card>
      <h2 className="text-base font-extrabold text-ink-900">Smart Progress section</h2>
      <p className="mt-0.5 text-xs text-ink-400">
        Keep claims to what SCORAM really does (accuracy by subject/topic, trends, Mistake Book, study plan). Don't promise scores, ranks or exam results.
        If you save nothing, the website shows the default copy. The image is optional (see Images below).
      </p>

      <div className="mt-4 space-y-4">
        <label className="flex items-center gap-2 text-sm font-semibold text-ink-900">
          <input type="checkbox" checked={published} onChange={(e) => setPublished(e.target.checked)} className="h-4 w-4 rounded border-primary-200 text-primary-600" />
          Show this section on the landing page
        </label>
        <FormField label="Heading" hint={`${heading.trim().length}/${max.heading}`}>
          <TextInput value={heading} maxLength={max.heading + 40} onChange={(e) => setHeading(e.target.value)} />
        </FormField>
        <FormField label="Description" hint={`${description.trim().length}/${max.description}`}>
          <TextArea rows={3} value={description} maxLength={max.description + 100} onChange={(e) => setDescription(e.target.value)} />
        </FormField>
        <FormField label="Highlights (one per line)" hint={`${list.length}/${max.highlights} highlights, up to ${max.highlight} characters each`}>
          <TextArea rows={5} value={highlights} onChange={(e) => setHighlights(e.target.value)} />
        </FormField>
        <div className="grid gap-4 sm:grid-cols-2">
          <FormField label="Button text (optional)" hint={`${ctaText.trim().length}/${max.ctaText}`}>
            <TextInput value={ctaText} maxLength={max.ctaText + 20} onChange={(e) => setCtaText(e.target.value)} />
          </FormField>
          <FormField label="Button link" hint="A site path like /login?mode=register, or an https:// address">
            <TextInput value={ctaLink} onChange={(e) => setCtaLink(e.target.value)} />
          </FormField>
        </div>
      </div>

      {notice && <div className="mt-4"><Alert type={notice.type}>{notice.message}</Alert></div>}

      <div className="mt-4 flex flex-wrap gap-2">
        <Button isLoading={busy} onClick={save}>Save section</Button>
        <Button variant="ghost" disabled={busy} onClick={() => setResetOpen(true)}><RotateCcw className="h-4 w-4" strokeWidth={2.25} /> Reset to default copy</Button>
      </div>

      <ConfirmDialog open={resetOpen} title="Reset Smart Progress copy?" message="Your saved text is removed and the website's default copy is shown again. The Smart Progress image (if any) is not affected." confirmLabel="Reset" onConfirm={doReset} onCancel={() => setResetOpen(false)} />
    </Card>
  );
}

export default function LandingContentManagement() {
  const { token } = useAdminAuth();
  const [data, setData] = useState(null);
  const [error, setError] = useState(null);
  const [version, setVersion] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    getLandingContentAdmin(token, { signal: controller.signal })
      .then((res) => { setData(res); setError(null); })
      .catch((err) => { if (err.name !== "AbortError") setError(friendlyError(err)); });
    return () => controller.abort();
  }, [token, version]);

  const groups = useMemo(() => {
    const map = new Map();
    (data?.slots || []).forEach((s) => { if (!map.has(s.group)) map.set(s.group, []); map.get(s.group).push(s); });
    return [...map.entries()];
  }, [data]);

  const refresh = async () => setVersion((v) => v + 1);

  return (
    <div>
      <PageHeader title="Landing Page" subtitle="Replace landing-page images and edit the Smart Progress section" />

      <div className="space-y-6 p-6">
        {error && <Alert type="error">{error}</Alert>}
        {!data && !error && <div className="flex justify-center py-16 text-ink-400" role="status" aria-label="Loading"><Loader2 className="h-6 w-6 animate-spin" /></div>}

        {data && (
          <>
            {/* key=updatedAt so the form re-initialises from the saved copy after a save / reset */}
            <SmartProgressEditor key={data.smartProgressUpdatedAt || "default"} initial={data.smartProgress} limits={data.limits} token={token} onSaved={refresh} />

            <p className="text-xs text-ink-400">
              An image that is not replaced always shows the website's built-in default. If an uploaded image ever fails to load for visitors, the default is shown instead.
              Founder photos are used exactly as uploaded — they are never edited or swapped between founders.
            </p>

            {groups.map(([group, slots]) => (
              <section key={group} aria-labelledby={`grp-${group}`}>
                <h2 id={`grp-${group}`} className="mb-3 text-sm font-extrabold uppercase tracking-wide text-ink-600">{group}</h2>
                <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3">
                  {slots.map((slot) => <SlotCard key={slot.key} slot={slot} limits={data.limits} token={token} onChanged={refresh} />)}
                </div>
              </section>
            ))}
          </>
        )}
      </div>
    </div>
  );
}
