import { useCallback, useEffect, useRef, useState } from "react";
import { useAuth } from "../context/AuthContext";
import { getBrowseProgress, startFreshBrowse, syncBrowseProgress, syncBrowseProgressKeepalive } from "../api/questionBrowseProgress";
import {
  MAX_ANSWERS_PER_REQUEST, capAnswers, dequeueSent, enqueueAnswer, isDefaultPosition, mergeAnswers, qKey,
  retryDelayMs, sanitizeFilters,
} from "../utils/browseProgress";

// PYQ BROWSING PROGRESS (website). Holds the student's chosen option per question ID for the whole PYQs page --
// so Slide mode's Previous/Next (which remounts a card each time) and Scroll mode both show what was picked --
// and keeps it, plus the browsing position (filters, last question, view mode), on the server so it survives
// leaving the page or closing the browser.
//
//   * Answers are saved incrementally (debounced ~0.4s), one entry per question, latest tap wins (server compares
//     clientUpdatedAt too). They are queued in localStorage until the server confirms; a failed send is retried
//     with back-off, on `online`, and on the next visit -- a pending answer is never silently dropped.
//   * The position is saved debounced, and flushed with `keepalive` when the tab is hidden / closed.
//   * Guests (not signed in) keep answers in memory for the visit only; nothing is sent anywhere.
//   * Identity is always the JWT on the server -- the localStorage mirror is keyed by user id so another student
//     signing in on the same browser never sees it.
const ANSWER_DEBOUNCE_MS = 400;
const POSITION_DEBOUNCE_MS = 1500;
const READY_TIMEOUT_MS = 4000; // never hold the page hostage to a slow progress request

const storageKey = (userId) => `scoram:qb:browse:${userId}`;

function readMirror(userId) {
  try {
    const raw = JSON.parse(localStorage.getItem(storageKey(userId)) || "null");
    return raw && typeof raw === "object" ? raw : null;
  } catch {
    return null;
  }
}

function writeMirror(userId, value) {
  try {
    localStorage.setItem(storageKey(userId), JSON.stringify(value));
  } catch {
    /* storage unavailable or full -- the server copy still exists */
  }
}

export function useQuestionBrowseProgress() {
  const { isAuthenticated, user } = useAuth();
  const userId = user?.userId ?? null;
  const enabled = Boolean(isAuthenticated && userId);

  const [answers, setAnswers] = useState({});
  const [snapshot, setSnapshot] = useState(null); // what the server had when the page opened (for Resume)
  const [ready, setReady] = useState(!enabled);
  const [syncState, setSyncState] = useState("idle"); // idle | saving | retrying

  const answersRef = useRef({});
  const pendingRef = useRef([]); // [{ questionId, option, at }] taps the server hasn't confirmed
  const positionRef = useRef(null); // position waiting to be sent
  const lastPositionRef = useRef(null); // last position queued (for dedupe + the offline mirror)
  const hasServerSessionRef = useRef(false);
  const inFlightRef = useRef(false);
  const attemptRef = useRef(0);
  const answerTimer = useRef(null);
  const positionTimer = useRef(null);
  const retryTimer = useRef(null);
  const mountedRef = useRef(true);
  const flushRef = useRef(() => {});

  const persist = useCallback(() => {
    if (!userId) return;
    writeMirror(userId, {
      answers: capAnswers(answersRef.current),
      pending: pendingRef.current,
      position: lastPositionRef.current,
    });
  }, [userId]);

  const safeSetSync = useCallback((state) => {
    if (mountedRef.current) setSyncState(state);
  }, []);

  const buildBody = useCallback(() => {
    const batch = pendingRef.current.slice(0, MAX_ANSWERS_PER_REQUEST);
    const position = positionRef.current;
    if (batch.length === 0 && !position) return null;
    const body = {};
    if (batch.length) {
      body.answers = batch.map((p) => ({ questionId: p.questionId, selectedOption: p.option, clientUpdatedAt: p.at }));
    }
    if (position) body.position = position;
    return { body, batch, position };
  }, []);

  const flush = useCallback(async () => {
    if (!enabled || inFlightRef.current) return;
    const built = buildBody();
    if (!built) {
      safeSetSync("idle");
      return;
    }
    inFlightRef.current = true;
    safeSetSync("saving");
    try {
      await syncBrowseProgress(built.body);
      hasServerSessionRef.current = true;
      pendingRef.current = dequeueSent(pendingRef.current, built.batch);
      if (positionRef.current === built.position) positionRef.current = null;
      attemptRef.current = 0;
      persist();
      inFlightRef.current = false;
      if (pendingRef.current.length || positionRef.current) {
        clearTimeout(answerTimer.current);
        answerTimer.current = setTimeout(() => flushRef.current(), 200); // more queued meanwhile / batch > 100
      } else {
        safeSetSync("idle");
      }
    } catch (err) {
      inFlightRef.current = false;
      if (err?.status === 400) {
        // The server will never accept this payload -- retrying forever would only block newer taps.
        pendingRef.current = dequeueSent(pendingRef.current, built.batch);
        if (positionRef.current === built.position) positionRef.current = null;
        persist();
        safeSetSync("idle");
        return;
      }
      // Network failure / 5xx / expired session: keep everything queued and try again later.
      persist();
      safeSetSync("retrying");
      clearTimeout(retryTimer.current);
      retryTimer.current = setTimeout(() => flushRef.current(), retryDelayMs(attemptRef.current++));
    }
  }, [enabled, buildBody, persist, safeSetSync]);

  useEffect(() => {
    flushRef.current = flush;
  }, [flush]);

  // ---- load (or reset when the signed-in student changes) ----
  useEffect(() => {
    mountedRef.current = true;
    answersRef.current = {};
    pendingRef.current = [];
    positionRef.current = null;
    lastPositionRef.current = null;
    hasServerSessionRef.current = false;
    setAnswers({});
    setSnapshot(null);
    if (!enabled) {
      setReady(true);
      return undefined;
    }
    setReady(false);

    let cancelled = false;
    let readyDone = false;
    const markReady = () => {
      if (!cancelled && !readyDone) {
        readyDone = true;
        setReady(true);
      }
    };
    const timeout = setTimeout(markReady, READY_TIMEOUT_MS);

    const mirror = readMirror(userId);
    pendingRef.current = Array.isArray(mirror?.pending) ? mirror.pending : [];

    getBrowseProgress()
      .then((server) => {
        if (cancelled) return;
        const merged = mergeAnswers(server?.answers, pendingRef.current);
        // Anything the student tapped while this request was in flight is newer than the server copy.
        answersRef.current = { ...merged, ...answersRef.current };
        setAnswers(answersRef.current);
        if (server) {
          hasServerSessionRef.current = true;
          if (!readyDone) {
            setSnapshot({
              filters: sanitizeFilters(server.filters),
              lastQuestionId: server.lastQuestionId || null,
              lastPosition: Number.isFinite(server.lastPosition) ? server.lastPosition : 0,
              viewMode: server.viewMode || null,
              answeredCount: Number.isFinite(server.answeredCount) ? server.answeredCount : Object.keys(merged).length,
            });
          }
        }
        markReady();
        if (pendingRef.current.length) flushRef.current(); // taps left over from a failed send last time
      })
      .catch(() => {
        if (cancelled) return;
        // Offline / API down: fall back to the last known state mirrored in this browser.
        const fallback = mergeAnswers(
          Object.entries(mirror?.answers || {}).map(([questionId, selectedOption]) => ({ questionId, selectedOption })),
          pendingRef.current,
        );
        answersRef.current = { ...fallback, ...answersRef.current };
        setAnswers(answersRef.current);
        if (mirror?.position && !readyDone) {
          setSnapshot({
            filters: sanitizeFilters(mirror.position.filters),
            lastQuestionId: mirror.position.lastQuestionId || null,
            lastPosition: Number.isFinite(mirror.position.lastPosition) ? mirror.position.lastPosition : 0,
            viewMode: mirror.position.viewMode || null,
            answeredCount: Object.keys(fallback).length,
          });
        }
        markReady();
        if (pendingRef.current.length) {
          setSyncState("retrying");
          retryTimer.current = setTimeout(() => flushRef.current(), retryDelayMs(attemptRef.current++));
        }
      });

    return () => {
      cancelled = true;
      clearTimeout(timeout);
    };
  }, [enabled, userId]);

  // ---- leaving the page: send what is queued; hiding the tab: send it with keepalive ----
  useEffect(() => {
    if (!enabled) return undefined;
    const onOnline = () => flushRef.current();
    const sendNow = () => {
      const built = buildBody();
      if (built) syncBrowseProgressKeepalive(built.body);
    };
    const onVisibility = () => {
      if (document.visibilityState === "hidden") sendNow();
    };
    window.addEventListener("online", onOnline);
    window.addEventListener("pagehide", sendNow);
    document.addEventListener("visibilitychange", onVisibility);
    return () => {
      window.removeEventListener("online", onOnline);
      window.removeEventListener("pagehide", sendNow);
      document.removeEventListener("visibilitychange", onVisibility);
    };
  }, [enabled, buildBody]);

  useEffect(
    () => () => {
      mountedRef.current = false;
      clearTimeout(answerTimer.current);
      clearTimeout(positionTimer.current);
      clearTimeout(retryTimer.current);
      flushRef.current(); // SPA navigation away from PYQs: send whatever is still queued
    },
    [],
  );

  // ---- public API ----
  const selectOption = useCallback(
    (questionId, option) => {
      const key = qKey(questionId);
      answersRef.current = { ...answersRef.current };
      if (option) answersRef.current[key] = option;
      else delete answersRef.current[key];
      setAnswers(answersRef.current);
      if (!enabled) return;
      if (option) {
        pendingRef.current = enqueueAnswer(pendingRef.current, { questionId: key, option, at: new Date().toISOString() });
        persist();
        clearTimeout(answerTimer.current);
        answerTimer.current = setTimeout(() => flushRef.current(), ANSWER_DEBOUNCE_MS);
      } else {
        // "Try Again": the student wiped the answer locally; nothing newer to send until they pick again.
        pendingRef.current = pendingRef.current.filter((p) => p.questionId !== key);
        persist();
      }
    },
    [enabled, persist],
  );

  const reportPosition = useCallback(
    ({ filters, lastQuestionId, lastPosition, viewMode }) => {
      if (!enabled) return;
      const payload = {
        filters: sanitizeFilters(filters),
        lastQuestionId: lastQuestionId || null,
        lastPosition: Math.max(0, Math.floor(lastPosition || 0)),
        viewMode: viewMode === "slide" ? "slide" : "scroll",
      };
      // Nothing worth remembering and no earlier session to overwrite: don't create an empty one.
      if (!hasServerSessionRef.current && isDefaultPosition(payload)) return;
      const key = JSON.stringify(payload);
      if (lastPositionRef.current && JSON.stringify(lastPositionRef.current) === key) return;
      lastPositionRef.current = payload;
      positionRef.current = { ...payload, clientUpdatedAt: new Date().toISOString() };
      persist();
      clearTimeout(positionTimer.current);
      positionTimer.current = setTimeout(() => flushRef.current(), POSITION_DEBOUNCE_MS);
    },
    [enabled, persist],
  );

  const startFresh = useCallback(async () => {
    if (enabled) {
      await startFreshBrowse(); // archives the old session on the server; throws if it couldn't
    }
    clearTimeout(answerTimer.current);
    clearTimeout(positionTimer.current);
    pendingRef.current = [];
    positionRef.current = null;
    lastPositionRef.current = null;
    hasServerSessionRef.current = enabled;
    answersRef.current = {};
    setAnswers({});
    setSnapshot(null);
    persist();
  }, [enabled, persist]);

  return {
    enabled,
    ready,
    answers,
    snapshot,
    syncState,
    answeredCount: Object.keys(answers).length,
    selectOption,
    reportPosition,
    startFresh,
  };
}
