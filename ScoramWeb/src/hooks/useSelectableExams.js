import { useMemo } from "react";
import { listExams, publicExamListKey } from "../api/exams";
import { useCatalog } from "./useCatalog";
import { useMyExamsScope } from "./useMyExamsScope";

// "MY EXAMS" -- the exams a screen may offer in an Exam dropdown, as [{ id, name, logoUrl }].
//   * Signed-in student -> ONLY their My Exams (no network call; already loaded). Unselected exams
//     never appear as options -- to see another exam's content they add it in My Exams.
//   * Signed-out visitor -> the public catalog (GET /api/exams), as before -- now through the shared
//     catalog cache, so the landing page's "popular exams" strip and this picker share one request.
// `ready` is false until the list can be trusted; `error` is true if the public fetch failed.
export function useSelectableExams() {
  const scope = useMyExamsScope();
  const publicQuery = useCatalog(publicExamListKey(), () => listExams(), { enabled: !scope.isScoped });

  const myExamOptions = useMemo(
    () => scope.exams.map((e) => ({ id: e.examId, name: e.examName, logoUrl: e.examLogoUrl })),
    [scope.exams]
  );

  if (scope.isScoped) {
    return { exams: myExamOptions, ready: scope.ready, error: false, isEmpty: scope.isEmpty, isScoped: true };
  }
  return {
    exams: publicQuery.data ?? EMPTY_EXAMS,
    ready: !publicQuery.loading,
    error: Boolean(publicQuery.error),
    isEmpty: false,
    isScoped: false,
  };
}

const EMPTY_EXAMS = [];
