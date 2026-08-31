import { apiFetch } from "./client";

function toQueryString(params = {}) {
  const query = new URLSearchParams();
  Object.entries(params).forEach(([key, value]) => {
    if (value === undefined || value === null || value === "") return;
    // Arrays (multi-select filters, e.g. examIds for "My Exams") become repeated keys -- see
    // api/questionBank.js's toQueryString, which this now matches.
    if (Array.isArray(value)) {
      if (value.length === 0) return;
      value.forEach((v) => query.append(key, v));
    } else {
      query.set(key, value);
    }
  });
  const qs = query.toString();
  return qs ? `?${qs}` : "";
}

// GET /api/practice-tests/templates?subjectId=&examId=&examIds=&page=&pageSize= -- admin-curated, browsable
export function listPracticeTestTemplates(params = {}, opts = {}) {
  return apiFetch(`/api/practice-tests/templates${toQueryString(params)}`, opts);
}

export function getPracticeTestTemplate(id, opts = {}) {
  return apiFetch(`/api/practice-tests/templates/${id}`, opts);
}

// POST /api/practice-tests/generate -- ad-hoc, student-configured. filters: { subjectId, topicId,
// examId, yearFrom, yearTo, difficulty, language, questionCount, durationMinutes,
// negativeMarkingRatio, isRandomOrder }
export function generatePracticeTest(filters) {
  return apiFetch("/api/practice-tests/generate", { method: "POST", auth: true, body: filters });
}

// POST /api/practice-tests/templates/{id}/start
export function startPracticeTestFromTemplate(id) {
  return apiFetch(`/api/practice-tests/templates/${id}/start`, { method: "POST", auth: true });
}

// The difficulty options offered in the Practice Test configuration form -- must match
// ScoramAPI.Enums.DifficultyLevel.
export const DIFFICULTY_OPTIONS = [
  { value: "", label: "Any difficulty" },
  { value: "Easy", label: "Easy" },
  { value: "Medium", label: "Medium" },
  { value: "Hard", label: "Hard" },
];

// Medium/language options for the same form -- must match ScoramAPI.Enums.PaperLanguage.
export const LANGUAGE_OPTIONS = [
  { value: "", label: "Any language" },
  { value: "Hindi", label: "Hindi" },
  { value: "English", label: "English" },
];
