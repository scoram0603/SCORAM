import { createContext, useCallback, useContext, useEffect, useMemo, useState } from "react";
import * as myExamsApi from "../api/myExams";
import { useAuth } from "./AuthContext";

// "MY EXAMS" -- loads a student's saved exam preferences once per session (mirrors AuthContext's
// own getMe() self-heal effect below) and exposes them everywhere a page needs a default exam
// context (see AppLayout.jsx for the onboarding redirect, and QuestionBankSearch/PreviousYearPapers/
// MockTests/PracticeTests for how each section applies `examIds` as its *default* filter -- an
// explicit filter the student picks on that page always overrides it, never the other way around).
const MyExamsContext = createContext(null);

export function MyExamsProvider({ children }) {
  const { isAuthenticated } = useAuth();
  const [exams, setExams] = useState([]);
  const [primaryExamId, setPrimaryExamId] = useState(null);
  // hasLoaded distinguishes "haven't checked yet" from "checked, and there are genuinely zero" --
  // AppLayout must not redirect to onboarding before this is true, or a student with exams already
  // configured would flash onto the onboarding screen for a moment on every page load.
  const [hasLoaded, setHasLoaded] = useState(false);
  const [isLoading, setIsLoading] = useState(false);

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
    } else {
      // Logged out (or a different student just logged in) -- clear immediately so the previous
      // session's selections can never leak into the next one (spec section 9: logging out and
      // another user logging in must not show the previous user's My Exams).
      setExams([]);
      setPrimaryExamId(null);
      setHasLoaded(false);
    }
  }, [isAuthenticated, refresh]);

  const save = useCallback(async ({ examIds, primaryExamId: newPrimaryId }) => {
    const res = await myExamsApi.setMyExams({ examIds, primaryExamId: newPrimaryId });
    setExams(res.exams || []);
    setPrimaryExamId(res.primaryExamId || null);
    setHasLoaded(true);
    return res;
  }, []);

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
      exams, examIds, primaryExamId, hasLoaded, hasConfigured, isLoading,
      refresh, save, addExam, removeExam, setPrimary,
    }),
    [exams, examIds, primaryExamId, hasLoaded, hasConfigured, isLoading, refresh, save, addExam, removeExam, setPrimary]
  );

  return <MyExamsContext.Provider value={value}>{children}</MyExamsContext.Provider>;
}

export function useMyExams() {
  const ctx = useContext(MyExamsContext);
  if (!ctx) throw new Error("useMyExams must be used within a MyExamsProvider");
  return ctx;
}
