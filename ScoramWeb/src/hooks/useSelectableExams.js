import { useEffect, useMemo, useState } from "react";
import { listExams } from "../api/exams";
import { useMyExamsScope } from "./useMyExamsScope";

// "MY EXAMS" -- the exams a screen may offer in an Exam dropdown, as [{ id, name, logoUrl }].
//   * Signed-in student -> ONLY their My Exams (no network call; already loaded). Unselected exams
//     never appear as options -- to see another exam's content they add it in My Exams.
//   * Signed-out visitor -> the public catalog (GET /api/exams), as before.
// `ready` is false until the list can be trusted; `error` is true if the public fetch failed.
export function useSelectableExams() {
  const scope = useMyExamsScope();
  const [publicExams, setPublicExams] = useState([]);
  const [publicStatus, setPublicStatus] = useState("loading");

  useEffect(() => {
    if (scope.isScoped) return undefined;
    const controller = new AbortController();
    setPublicStatus("loading");
    listExams({ signal: controller.signal })
      .then((list) => { setPublicExams(list); setPublicStatus("ready"); })
      .catch((err) => { if (err.name !== "AbortError") setPublicStatus("error"); });
    return () => controller.abort();
  }, [scope.isScoped]);

  const myExamOptions = useMemo(
    () => scope.exams.map((e) => ({ id: e.examId, name: e.examName, logoUrl: e.examLogoUrl })),
    [scope.exams]
  );

  if (scope.isScoped) {
    return { exams: myExamOptions, ready: scope.ready, error: false, isEmpty: scope.isEmpty, isScoped: true };
  }
  return { exams: publicExams, ready: publicStatus !== "loading", error: publicStatus === "error", isEmpty: false, isScoped: false };
}
