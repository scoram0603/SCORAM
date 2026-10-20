import { useCallback, useMemo, useState } from "react";
import { useLandingContent } from "./useLandingContent";
import { resolveSlotSources } from "../config/landingSlots";

/**
 * Image source for a landing slot with centralised fallback.
 *   src       -> the URL to render now (null when every candidate is exhausted)
 *   onError   -> wire to <img onError>; advances to the next candidate (admin URL -> bundled default), each
 *                candidate tried exactly once, so there are no endless retry loops
 *   exhausted -> true when nothing is left to show (render a placeholder / initials instead)
 * Before the configuration has loaded the bundled default is shown, so the page never waits for the API.
 */
export function useSlotImage(slotKey, { fallbackSrc = null } = {}) {
  const { media } = useLandingContent();
  const sources = useMemo(
    () => resolveSlotSources(slotKey, media, { fallbackSrc }),
    [slotKey, media, fallbackSrc],
  );
  const signature = sources.join("|");
  // Progress is remembered per candidate list: when the list changes (the admin URL arrived or was replaced) the
  // derived stage is 0 immediately -- no render with a stale index, no flash of the placeholder.
  const [progress, setProgress] = useState({ signature, stage: 0 });
  const stage = progress.signature === signature ? progress.stage : 0;

  const onError = useCallback(
    () => setProgress((p) => ({ signature, stage: (p.signature === signature ? p.stage : 0) + 1 })),
    [signature],
  );
  const src = stage < sources.length ? sources[stage] : null;
  return { src, onError, exhausted: src === null };
}
