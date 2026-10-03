import { useAuth } from "../context/AuthContext";
import { useMyExams } from "../context/MyExamsContext";

// "MY EXAMS" -- one place that answers "is this visitor's exam content scoped, and is that scope
// ready / empty?" for every exam-specific screen (PYP, Question Bank, Tests, Mock Tests, Practice,
// Groups, Home, Search...).
//
// My Exams is a STRICT scope for signed-in students: the API only ever returns content of their
// exams (see ScoramAPI MyExamScopeService), so screens must
//   1. wait for `ready` before fetching (otherwise a first request races the scope load), and
//   2. show the "Choose My Exams" prompt instead of an empty/misleading list when `isEmpty`.
// A signed-out visitor is not scoped and browses the public catalog as before.
export function useMyExamsScope() {
  const { isAuthenticated } = useAuth();
  const { exams, examIds, hasLoaded, hasConfigured, loadError } = useMyExams();

  const isScoped = isAuthenticated;
  const ready = !isScoped || hasLoaded;
  // A failed load is NOT "empty" -- the API still scopes by the saved selection, so just fetch.
  const isEmpty = isScoped && hasLoaded && !loadError && !hasConfigured;

  return { isScoped, ready, isEmpty, exams, examIds };
}
