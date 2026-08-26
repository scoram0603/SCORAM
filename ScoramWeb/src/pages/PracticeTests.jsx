import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { ArrowLeft, Loader2, ChevronDown, Sparkles, Play } from "lucide-react";
import { getQuestionBankSubjects, getQuestionBankTopics, getQuestionBankExams } from "../api/questionBank";
import { listPracticeTestTemplates, DIFFICULTY_OPTIONS, LANGUAGE_OPTIONS } from "../api/practiceTests";

const QUESTION_COUNT_OPTIONS = [10, 20, 30, 50];
const DURATION_OPTIONS = [10, 20, 30, 45, 60];

export default function PracticeTests() {
  const navigate = useNavigate();
  const [subjects, setSubjects] = useState([]);
  const [topics, setTopics] = useState([]);
  const [exams, setExams] = useState([]);

  const [subjectId, setSubjectId] = useState("");
  const [topicId, setTopicId] = useState("");
  const [examId, setExamId] = useState("");
  const [difficulty, setDifficulty] = useState("");
  const [language, setLanguage] = useState(""); // "" | "Hindi" | "English"
  const [questionCount, setQuestionCount] = useState(20);
  const [durationMinutes, setDurationMinutes] = useState(20);
  const [generating, setGenerating] = useState(false);
  const [error, setError] = useState(null);

  const [templates, setTemplates] = useState(null);
  const [startingTemplateId, setStartingTemplateId] = useState(null);

  useEffect(() => {
    getQuestionBankSubjects().then(setSubjects).catch(() => {});
    getQuestionBankExams().then(setExams).catch(() => {});
    listPracticeTestTemplates({ page: 1, pageSize: 10 }).then((res) => setTemplates(res.items)).catch(() => setTemplates([]));
  }, []);

  useEffect(() => {
    if (!subjectId) {
      setTopics([]);
      return;
    }
    getQuestionBankTopics(subjectId).then(setTopics).catch(() => setTopics([]));
  }, [subjectId]);

  function handleGenerate(e) {
    e.preventDefault();
    const subjectName = subjects.find((s) => s.id === subjectId)?.name || null;
    const topicName = topics.find((t) => t.id === topicId)?.name || null;
    const examName = exams.find((x) => x.id === examId)?.name || null;
    const difficultyLabel = DIFFICULTY_OPTIONS.find((d) => d.value === difficulty)?.label || null;
    const languageLabel = LANGUAGE_OPTIONS.find((l) => l.value === language)?.label || null;

    navigate("/tests/instructions/practice-adhoc/adhoc", {
      state: {
        filters: {
          subjectId: subjectId || null,
          topicId: topicId || null,
          examId: examId || null,
          difficulty: difficulty || null,
          language: language || null,
          questionCount,
          durationMinutes,
          negativeMarkingRatio: 0,
          isRandomOrder: true,
        },
        labels: { subjectName, topicName, examName, difficultyLabel, languageLabel },
      },
    });
  }

  function handleStartTemplate(id) {
    navigate(`/tests/instructions/practice-template/${id}`);
  }

  return (
    <div className="px-4 pb-10 pt-4 sm:px-6 lg:px-8 lg:pt-6">
      <button type="button" onClick={() => navigate("/tests")} className="flex items-center gap-1.5 text-sm font-semibold text-secondary-500">
        <ArrowLeft className="h-4 w-4" strokeWidth={2.5} />
        Tests
      </button>

      <h1 className="mt-3 text-xl font-extrabold text-ink-900 sm:text-2xl">Practice Tests</h1>
      <p className="mt-1 text-sm text-ink-400">Choose a subject, topic or exam and practice questions at your own pace.</p>

      <form onSubmit={handleGenerate} className="mt-5 rounded-xl2 border border-primary-100 bg-white p-4 shadow-card sm:p-5">
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <Dropdown label="Subject" value={subjectId} onChange={(v) => { setSubjectId(v); setTopicId(""); }} placeholder="Any subject">
            {subjects.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
          </Dropdown>
          <Dropdown label="Topic" value={topicId} onChange={setTopicId} placeholder="Any topic" disabled={!subjectId}>
            {topics.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}
          </Dropdown>
          <Dropdown label="Exam" value={examId} onChange={setExamId} placeholder="Any exam">
            {exams.map((e) => <option key={e.id} value={e.id}>{e.name}</option>)}
          </Dropdown>
          <Dropdown label="Difficulty" value={difficulty} onChange={setDifficulty} placeholder={DIFFICULTY_OPTIONS[0].label}>
            {DIFFICULTY_OPTIONS.slice(1).map((d) => <option key={d.value} value={d.value}>{d.label}</option>)}
          </Dropdown>
          <Dropdown label="Medium" value={language} onChange={setLanguage} placeholder={LANGUAGE_OPTIONS[0].label}>
            {LANGUAGE_OPTIONS.slice(1).map((l) => <option key={l.value} value={l.value}>{l.label}</option>)}
          </Dropdown>
          <Dropdown label="Number of Questions" value={String(questionCount)} onChange={(v) => setQuestionCount(Number(v))}>
            {QUESTION_COUNT_OPTIONS.map((n) => <option key={n} value={n}>{n} questions</option>)}
          </Dropdown>
          <Dropdown label="Duration" value={String(durationMinutes)} onChange={(v) => setDurationMinutes(Number(v))}>
            {DURATION_OPTIONS.map((n) => <option key={n} value={n}>{n} minutes</option>)}
          </Dropdown>
        </div>

        {error && <p className="mt-3 text-sm text-red-600">{error}</p>}

        <button
          type="submit"
          disabled={generating}
          className="mt-4 flex w-full items-center justify-center gap-2 rounded-xl2 bg-mint-500 py-3 text-sm font-bold text-white transition-colors hover:bg-mint-600 disabled:opacity-60"
        >
          {generating ? <Loader2 className="h-4 w-4 animate-spin" strokeWidth={2.5} /> : <Sparkles className="h-4 w-4" strokeWidth={2.25} />}
          Start Practice Test
        </button>
      </form>

      {templates?.length > 0 && (
        <div className="mt-6">
          <h2 className="text-sm font-bold text-ink-900">Curated Practice Tests</h2>
          <div className="mt-2 flex flex-col gap-2">
            {templates.map((t) => (
              <div key={t.id} className="flex items-center justify-between gap-3 rounded-xl2 border border-primary-100 bg-white p-4 shadow-card">
                <div className="min-w-0">
                  <p className="truncate text-sm font-bold text-ink-900">{t.title}</p>
                  <p className="mt-0.5 text-xs text-ink-400">
                    {t.questionCount} questions · {t.durationMinutes} min
                    {t.subject ? ` · ${t.subject}` : ""}
                    {t.difficulty ? ` · ${t.difficulty}` : ""}
                  </p>
                </div>
                <button
                  type="button"
                  onClick={() => handleStartTemplate(t.id)}
                  disabled={startingTemplateId === t.id}
                  className="flex shrink-0 items-center gap-1.5 rounded-xl2 bg-primary-600 px-3.5 py-2 text-xs font-semibold text-white disabled:opacity-60"
                >
                  {startingTemplateId === t.id ? <Loader2 className="h-3.5 w-3.5 animate-spin" strokeWidth={2.5} /> : <Play className="h-3.5 w-3.5" strokeWidth={2.25} />}
                  Start
                </button>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}

function Dropdown({ label, value, onChange, placeholder, disabled, children }) {
  return (
    <label className="block">
      <span className="mb-1 block text-xs font-semibold text-ink-600">{label}</span>
      <span className="relative block">
        <select
          value={value}
          disabled={disabled}
          onChange={(e) => onChange(e.target.value)}
          className="h-11 w-full appearance-none rounded-xl2 border border-primary-100 bg-white px-3 pr-8 text-sm text-ink-900 focus:border-secondary-500 disabled:bg-primary-50 disabled:text-ink-400"
        >
          {placeholder && <option value="">{placeholder}</option>}
          {children}
        </select>
        <ChevronDown className="pointer-events-none absolute right-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-ink-400" strokeWidth={2} />
      </span>
    </label>
  );
}
