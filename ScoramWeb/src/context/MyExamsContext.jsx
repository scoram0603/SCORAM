import { createContext, useCallback, useContext, useEffect, useMemo, useState } from "react";
import * as myExamsApi from "../api/myExams";
import { useAuth } from "./AuthContext";

// "MY EXAMS" -- loads a student's saved exam preferences once per session (mirrors AuthContext's
// own getMe() self-heal effect below) and exposes them everywhere. My Exams is a STRICT content
// scope, not a default filter: the API itself only returns content of these exams (see ScoramAPI
// MyExamScopeService), and exam-specific screens use useMyExamsScope() to wait for it, show a
// "Choose My Exams" prompt when it is empty, and only ever offer these exams as filter options.
// See AppLayout.jsx for the first-time onboarding redirect.
const MyExamsContext = createContext(null);

// "Skip for Now" on the onboarding screen doesn't set any exams (My Exams stays EMPTY -- it never
// silently selects everything) -- it just needs to stop AppLayout from bouncing the student straight
// back to /select-exams. Saving an empty list from the My Exams screen counts as the same deliberate
// choice. Scoped to sessionStorage (not localStorage) per user id: it clears itself when the
// tab/browser closes, so a student who skips gets a gentle ask again next time, rather than never
// being asked again; in the meantime every exam screen shows a "Choose My Exams" prompt. Keyed by
// userId so it can never leak into a different student's session on a shared device.
const SKIP_KEY_PREFIX = "scoram_skip_exams_";

function readSkipped(userId) {
  if (!userId) return false;
  try {
    return sessionStorage.getItem(SKIP_KEY_PREFIX + userId) === "1";
  } catch {
    return false; // sessionStorage unavailable (private browsing etc.) -- just re-prompt every time
  }
}

export function MyExamsProvider({ children }) {
  const { isAuthenticated, user } = useAuth();
  const [exams, setExams] = useState([]);
  const [primaryExamId, setPrimaryExamId] = useState(null);
  // hasLoaded distinguishes "haven't checked yet" from "checked, and there are genuinely zero" --
  // AppLayout must not redirect to onboarding before this is true, or a student with exams already
  // configured would flash onto the onboarding screen for a moment on every page load.
  const [hasLoaded, setHasLoaded] = useState(false);
  const [isLoading, setIsLoading] = useState(false);
  const [skipped, setSkipped] = useState(false);
  // True when the last load FAILED. A failed load must not look like "no exams selected" (that would
  // show a wrong "Choose My Exams" prompt) -- screens then just fetch normally; the API scopes by
  // the saved selection server-side either way.
  const [loadError, setLoadError] = useState(false);

  const refresh = useCallback(async () => {
    setIsLoading(true);
    try {
      const res = await myExamsApi.getMyExams();
      setExams(res.exams || []);
      setPrimaryExamId(res.primaryExamId || null);
      setHasLoaded(true);
      setLoadError(false);
      return res;
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    if (isAuthenticated) {
      refresh().catch(() => { setLoadError(true); setHasLoaded(true); }); // a failed load still counts as
      // "checked" -- don't trap a student in a redirect loop to onboarding just because one request
      // hiccuped (loadError keeps screens from treating it as an empty My Exams).
      setSkipped(readSkipped(user?.userId));
    } else {
      // Logged out (or a different student just logged in) -- clear immediately so the previous
      // session's selections can never leak into the next one (spec section 9: logging out and
      // another user logging in must not show the previous user's My Exams).
      setExams([]);
      setPrimaryExamId(null);
      setHasLoaded(false);
      setLoadError(false);
      setSkipped(false);
    }
  }, [isAuthenticated, user?.userId, refresh]);

  const skipOnboarding = useCallback(() => {
    setSkipped(true);
    if (user?.userId) {
      try {
        sessionStorage.setItem(SKIP_KEY_PREFIX + user.userId, "1");
      } catch {
        // ignore -- skip just won't survive a reload this session
      }
    }
  }, [user]);

  const save = useCallback(async ({ examIds, primaryExamId: newPrimaryId }) => {
    const res = await myExamsApi.setMyExams({ examIds, primaryExamId: newPrimaryId });
    setExams(res.exams || []);
    setPrimaryExamId(res.primaryExamId || null);
    setHasLoaded(true);
    setLoadError(false);
    // Exams chosen -> the skip flag is moot, clean it up. Saved EMPTY (the student removed every
    // exam on purpose) -> treat it as a deliberate "none for now", so AppLayout doesn't bounce them
    // into onboarding; screens show the Choose My Exams prompt instead.
    const nowEmpty = (res.exams || []).length === 0;
    setSkipped(nowEmpty);
    if (user?.userId) {
      try {
        if (nowEmpty) sessionStorage.setItem(SKIP_KEY_PREFIX + user.userId, "1");
        else sessionStorage.removeItem(SKIP_KEY_PREFIX + user.userId);
      } catch {
        // ignore
      }
    }
    return res;
  }, [user]);

  const addExam = useCallback(async (examId) => {
    const res = await myExamsApi.addMyExam(examId);
    setExams(res.exams || []);
    setPrimaryExamId(res.primaryExamId || null);
    return res;
  }, []);

  const removeExam = useCallback(async (examId) => {
    await myExamsApi.removeMyExam(examId);
    return refresh();
  }, [refresh]);

  const setPrimary = useCallback(async (examId) => {
    const res = await myExamsApi.setPrimaryExam(examId);
    setExams(res.exams || []);
    setPrimaryExamId(res.primaryExamId || null);
    return res;
  }, []);

  const examIds = useMemo(() => exams.map((e) => e.examId), [exams]);
  const hasConfigured = hasLoaded && exams.length > 0;

  const value = useMemo(
    () => ({
      exams, examIds, primaryExamId, hasLoaded, hasConfigured, isLoading, skipped, loadError,
      refresh, save, addExam, removeExam, setPrimary, skipOnboarding,
    }),
    [
      exams, examIds, primaryExamId, hasLoaded, hasConfigured, isLoading, skipped, loadError,
      refresh, save, addExam, removeExam, setPrimary, skipOnboarding,
    ]
  );

  return <MyExamsContext.Provider value={value}>{children}</MyExamsContext.Provider>;
}

export function useMyExams() {
  const ctx = useContext(MyExamsContext);
  if (!ctx) throw new Error("useMyExams must be used within a MyExamsProvider");
  return ctx;
}
