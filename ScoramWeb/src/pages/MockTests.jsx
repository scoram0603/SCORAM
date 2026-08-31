import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { ArrowLeft, Loader2, Clock, Play, RotateCcw, Users } from "lucide-react";
import { listMockTests } from "../api/mockTests";
import { listExams } from "../api/exams";
import BookmarkButton from "../components/questions/BookmarkButton";
import SearchableSelect from "../components/ui/SearchableSelect";
import { useMyExams } from "../context/MyExamsContext";
import { useDefaultToMyExams } from "../hooks/useDefaultToMyExams";

const AVAILABILITY_STYLES = {
  Upcoming: "bg-secondary-50 text-secondary-500",
  Live: "bg-mint-50 text-mint-500",
  Completed: "bg-ink-100 text-ink-400",
};

const LANGUAGE_OPTIONS = [
  { value: "Hindi", label: "Hindi" },
  { value: "English", label: "English" },
];

export default function MockTests() {
  const navigate = useNavigate();
  const [tests, setTests] = useState(null);
  const [status, setStatus] = useState("loading");
  const [language, setLanguage] = useState([]); // SearchableSelect works with arrays; single value here
  const [exams, setExams] = useState([]);
  const [examIds, setExamIds] = useState([]);

  // "MY EXAMS" -- this section had no exam filter at all before; it now defaults to the student's
  // saved exams the first time the page loads (see useDefaultToMyExams's own comment for why this
  // only ever applies once per visit, and MockTest.ExamId's own comment in
  // Models/MockTestModels.cs for why this is matched by exam ID rather than the ExamName string
  // every test already carries).
  const { examIds: myExamIds, hasLoaded: myExamsLoaded } = useMyExams();
  useDefaultToMyExams({
    hasExplicitFilter: false,
    myExamIds,
    hasLoaded: myExamsLoaded,
    applyDefault: setExamIds,
  });

  useEffect(() => {
    listExams().then(setExams).catch(() => setExams([]));
  }, []);

  useEffect(() => {
    setStatus("loading");
    listMockTests({ page: 1, pageSize: 50, language: language[0], examIds })
      .then((res) => {
        setTests(res.items);
        setStatus("success");
      })
      .catch(() => setStatus("error"));
  }, [language, examIds]);

  function handleStart(id) {
    navigate(`/tests/instructions/mock/${id}`);
  }

  function handleBookmarkChange(id, isBookmarked) {
    setTests((prev) => prev.map((t) => (t.id === id ? { ...t, isBookmarked } : t)));
  }

  return (
    <div className="px-4 pb-10 pt-4 sm:px-6 lg:px-8 lg:pt-6">
      <button type="button" onClick={() => navigate("/tests")} className="flex items-center gap-1.5 text-sm font-semibold text-secondary-500">
        <ArrowLeft className="h-4 w-4" strokeWidth={2.5} />
        Tests
      </button>

      <div className="mt-3 flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-xl font-extrabold text-ink-900 sm:text-2xl">Mock Tests</h1>
          <p className="mt-1 text-sm text-ink-400">Experience the real exam pattern with timed mock tests.</p>
        </div>
        <div className="flex flex-wrap gap-3">
          <div className="w-48">
            <SearchableSelect
              label="Exam"
              placeholder="All exams"
              options={exams.map((e) => ({ value: e.id, label: e.name }))}
              selected={examIds}
              onChange={setExamIds}
              multi
            />
          </div>
          <div className="w-40">
            <SearchableSelect
              label="Medium"
              placeholder="Any language"
              options={LANGUAGE_OPTIONS}
              selected={language}
              onChange={setLanguage}
              multi={false}
            />
          </div>
        </div>
      </div>

      <div className="mt-5">
        {status === "loading" && (
          <div className="flex justify-center py-16 text-ink-400">
            <Loader2 className="h-6 w-6 animate-spin" strokeWidth={2.25} />
          </div>
        )}
        {status === "error" && <p className="rounded-xl2 border border-red-100 bg-red-50 p-4 text-sm text-red-600">Couldn't load Mock Tests right now.</p>}
        {status === "success" && tests.length === 0 && <p className="rounded-xl2 border border-primary-100 bg-white p-4 text-sm text-ink-400">No mock tests available yet — check back soon.</p>}

        <div className="flex flex-col gap-3">
          {tests?.map((t) => {
            const isCompleted = t.availabilityStatus === "Completed";
            const outOfAttempts = t.maxAttempts != null && t.myAttemptCount != null && t.myAttemptCount >= t.maxAttempts;
            const disabled = isCompleted || outOfAttempts;
            return (
              <div key={t.id} className="rounded-xl2 border border-primary-100 bg-white p-4 shadow-card">
                <div className="flex flex-wrap items-start justify-between gap-2">
                  <div className="min-w-0">
                    <div className="flex flex-wrap items-center gap-1.5">
                      <span className={`rounded-md px-2 py-0.5 text-[11px] font-bold ${AVAILABILITY_STYLES[t.availabilityStatus] || "bg-primary-50 text-primary-600"}`}>
                        {t.availabilityStatus}
                      </span>
                      <span className="text-[11px] font-semibold text-ink-400">{t.examName}</span>
                      {t.language && <span className="rounded-md bg-mint-50 px-2 py-0.5 text-[11px] font-bold text-mint-600">{t.language}</span>}
                    </div>
                    <p className="mt-1 text-sm font-bold text-ink-900">{t.title}</p>
                    <p className="mt-0.5 flex flex-wrap items-center gap-x-1 gap-y-0.5 text-xs text-ink-400">
                      <Clock className="h-3.5 w-3.5" strokeWidth={2} />
                      {t.durationMinutes} min · {t.questionCount} questions
                      {t.maxAttempts != null && ` · ${t.myAttemptCount ?? 0}/${t.maxAttempts} attempts used`}
                      <span className="mx-0.5 text-primary-200">·</span>
                      <Users className="h-3.5 w-3.5" strokeWidth={2} />
                      {t.attemptCount > 0 ? `Attempted by ${t.attemptCount} student${t.attemptCount === 1 ? "" : "s"}` : "No attempts yet"}
                    </p>
                  </div>

                  <div className="flex shrink-0 items-center gap-2">
                    <BookmarkButton
                      type="mockTest"
                      id={t.id}
                      isBookmarked={t.isBookmarked}
                      size="sm"
                      onChange={(isBookmarked) => handleBookmarkChange(t.id, isBookmarked)}
                      onRequireLogin={() => navigate(`/login?redirect=/tests/mock`)}
                    />
                    <button
                      type="button"
                      onClick={() => handleStart(t.id)}
                      disabled={disabled}
                      className="flex items-center gap-1.5 rounded-xl2 bg-primary-600 px-3.5 py-2 text-xs font-semibold text-white disabled:opacity-40"
                    >
                      {t.myAttemptCount > 0 ? (
                        <RotateCcw className="h-3.5 w-3.5" strokeWidth={2.25} />
                      ) : (
                        <Play className="h-3.5 w-3.5" strokeWidth={2.25} />
                      )}
                      {outOfAttempts ? "No attempts left" : isCompleted ? "Closed" : t.myAttemptCount > 0 ? "Attempt again" : "Start"}
                    </button>
                  </div>
                </div>
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
}
