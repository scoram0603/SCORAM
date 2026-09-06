import { createContext, useCallback, useContext, useEffect, useMemo, useState } from "react";
import * as myExamsApi from "../api/myExams";
import { useAuth } from "./AuthContext";

// "MY EXAMS" -- loads a student's saved exam preferences once per session (mirrors AuthContext's
// own getMe() self-heal effect below) and exposes them everywhere a page needs a default exam
// context (see AppLayout.jsx for the onboarding redirect, and QuestionBankSearch/PreviousYearPapers/
// MockTests/PracticeTests for how each section applies `examIds` as its *default* filter -- an
// explicit filter the student picks on that page always overrides it, never the other way around).
const MyExamsContext = createContext(null);

// "Skip for now" on the onboarding screen doesn't set any exams -- it just needs to stop AppLayout
// from bouncing the student straight back to /select-exams. Scoped to sessionStorage (not
// localStorage) per user id: it clears itself when the tab/browser closes, so a student who skips
// gets asked again next time they open the app, rather than never being asked again. Keyed by
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

  const refresh = useCallback(async () => {
    setIsLoading(true);
    try {
      const res = await myExamsApi.getMyExams();
      setExams(res.exams || []);
      setPrimaryExamId(res.primaryExamId || null);
      setHasLoaded(true);
      return res;
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    if (isAuthenticated) {
      refresh().catch(() => setHasLoaded(true)); // a failed load still counts as "checked" -- don't
      // trap a student in a redirect loop to onboarding just because one request hiccuped; the
      // section pages simply won't have a My Exams default for the rest of this load.
      setSkipped(readSkipped(user?.userId));
    } else {
      // Logged out (or a different student just logged in) -- clear immediately so the previous
      // session's selections can never leak into the next one (spec section 9: logging out and
      // another user logging in must not show the previous user's My Exams).
      setExams([]);
      setPrimaryExamId(null);
      setHasLoaded(false);
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
    setSkipped(false); // exams are configured now -- the skip flag is moot, clean it up
    if (user?.userId) {
      try {
        sessionStorage.removeItem(SKIP_KEY_PREFIX + user.userId);
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
      exams, examIds, primaryExamId, hasLoaded, hasConfigured, isLoading, skipped,
      refresh, save, addExam, removeExam, setPrimary, skipOnboarding,
    }),
    [
      exams, examIds, primaryExamId, hasLoaded, hasConfigured, isLoading, skipped,
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
