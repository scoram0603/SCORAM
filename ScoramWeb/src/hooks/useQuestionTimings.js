import { useCallback, useEffect, useRef } from "react";
import { patchAttemptTimings } from "../api/smartProgress";

const SEND_EVERY_MS = 20000;

// SMART PROGRESS -- per-question time for the live test. Counts seconds only while a question is on screen AND
// the tab is visible, keeps RUNNING TOTALS per studentAnswerId (the server only ever accepts a total that grows),
// and reports them periodically, when the tab is hidden, and just before submit.
//
// It is strictly fire-and-forget: it never awaits, never throws, never touches answers, the countdown, auto-submit
// or submission. If a request fails the totals simply go out with the next one.
export function useQuestionTimings({ attemptId, questions, currentId, enabled }) {
  const totals = useRef({}); // studentAnswerId -> seconds (float)
  const sent = useRef({}); // studentAnswerId -> last whole seconds the server acknowledged
  const activeId = useRef(null);
  const startedAt = useRef(null);
  const seeded = useRef(false);

  // Resume: continue from whatever the server already holds for each question.
  useEffect(() => {
    if (!enabled || seeded.current || !questions.length) return;
    questions.forEach((q) => {
      if (q.timeSpentSeconds > 0) {
        totals.current[q.id] = q.timeSpentSeconds;
        sent.current[q.id] = q.timeSpentSeconds;
      }
    });
    seeded.current = true;
  }, [enabled, questions]);

  const commit = useCallback(() => {
    if (activeId.current && startedAt.current != null) {
      const now = Date.now();
      totals.current[activeId.current] = (totals.current[activeId.current] || 0) + (now - startedAt.current) / 1000;
      startedAt.current = now;
    }
  }, []);

  const flush = useCallback(() => {
    commit();
    const timings = [];
    Object.entries(totals.current).forEach(([id, secs]) => {
      const whole = Math.round(secs);
      if (whole > (sent.current[id] || 0)) timings.push({ studentAnswerId: id, timeSpentSeconds: Math.min(whole, 86400) });
    });
    if (timings.length === 0) return;
    try {
      patchAttemptTimings(attemptId, timings.slice(0, 300))
        .then(() => {
          timings.forEach((t) => {
            sent.current[t.studentAnswerId] = Math.max(sent.current[t.studentAnswerId] || 0, t.timeSpentSeconds);
          });
        })
        .catch(() => {});
    } catch {
      // never let timing reporting disturb the test
    }
  }, [attemptId, commit]);

  // Question changes: bank the time of the previous one, start counting the new one.
  useEffect(() => {
    if (!enabled) return undefined;
    commit();
    activeId.current = currentId || null;
    startedAt.current = typeof document !== "undefined" && document.hidden ? null : Date.now();
    return undefined;
  }, [currentId, enabled, commit]);

  useEffect(() => {
    if (!enabled) return undefined;
    function onVisibility() {
      if (document.hidden) {
        flush();
        startedAt.current = null;
      } else if (activeId.current) {
        startedAt.current = Date.now();
      }
    }
    document.addEventListener("visibilitychange", onVisibility);
    const interval = setInterval(flush, SEND_EVERY_MS);
    return () => {
      document.removeEventListener("visibilitychange", onVisibility);
      clearInterval(interval);
    };
  }, [enabled, flush]);

  return { flush };
}
