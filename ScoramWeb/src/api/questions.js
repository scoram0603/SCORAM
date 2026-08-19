import { apiFetch } from "./client";

// GET /api/questions?examName=&year=&paperCode=&questionNumber=&subject=&topic=&difficultyLevel=&keyword=&page=&pageSize=
// See ScoramAPI/Controllers/QuestionsController.cs -> Search()
export function searchQuestions(params = {}, { signal } = {}) {
  const query = new URLSearchParams();

  Object.entries(params).forEach(([key, value]) => {
    if (value !== undefined && value !== null && value !== "") {
      query.set(key, value);
    }
  });

  const qs = query.toString();
  return apiFetch(`/api/questions${qs ? `?${qs}` : ""}`, { signal });
}

// GET /api/questions/instant-search?q=... -- typo-tolerant, Meilisearch-backed search bar
export function instantSearch(q, { signal } = {}) {
  if (!q?.trim()) return Promise.resolve([]);
  return apiFetch(`/api/questions/instant-search?q=${encodeURIComponent(q)}`, { signal });
}

// GET /api/questions/today -- deterministic daily pick, same for everyone, changes at midnight UTC
export function getTodaysChallenge({ signal } = {}) {
  return apiFetch("/api/questions/today", { signal });
}

// GET /api/questions/{id} — id is a GUID string, e.g. "8f14e45f-ceea-467e-add1-000000000001"
export function getQuestionById(id, { signal } = {}) {
  return apiFetch(`/api/questions/${id}`, { signal });
}
