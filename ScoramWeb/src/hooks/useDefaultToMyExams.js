import { useEffect, useRef } from "react";

// "MY EXAMS" -- applies the student's saved exam preferences as a section's *default* exam
// filter, exactly once per page visit (mount), and only when the page was opened with no explicit
// exam filter already present. Deliberately NOT reactive to every later value change: once a
// student explicitly clears the exam filter on this page (choosing "All Exams" for this browsing
// session), that choice sticks for the rest of the visit -- landing back on My Exams only happens
// by leaving and returning. This is the filter-precedence rule from the feature spec: an explicit
// filter always outranks My Exams, and My Exams always outranks "All Exams" only shown when
// nothing else applies -- see QuestionBankSearch/PreviousYearPapers/MockTests/PracticeTests for
// where each section wires this in.
export function useDefaultToMyExams({ hasExplicitFilter, myExamIds, hasLoaded, applyDefault }) {
  const appliedRef = useRef(false);

  useEffect(() => {
    if (appliedRef.current || !hasLoaded) return;
    appliedRef.current = true;
    if (!hasExplicitFilter && myExamIds.length > 0) applyDefault(myExamIds);
    // Deliberately mount-once (guarded by appliedRef) -- see this hook's own comment above for why
    // reacting to every later change to these values would fight a student's explicit "All Exams".
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [hasLoaded]);
}
