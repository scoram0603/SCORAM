import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { Plus, Pencil, Trash2, RotateCcw, X, ChevronLeft, ChevronRight, UploadCloud, Tags, Flag, AlertTriangle } from "lucide-react";
import { useAdminAuth } from "../context/AdminAuthContext";
import {
  listQuestionBankQuestions, createQuestionBankQuestion, updateQuestionBankQuestion, deleteQuestionBankQuestion,
  listSubjects, listTopics, getQuestionBankStats, uploadQuestionBankImages,
} from "../api/questionBank";
import { listExams } from "../api/exams";
import { PageHeader, Card, Button, FormField, TextInput, TextArea, Select, Alert, friendlyError } from "../components/AdminUI";
import ImagePickerField from "../components/ImagePickerField";
import EditImageField, { imgSrc } from "../components/EditImageField";

const PAGE_SIZE = 20;

// Section 7-8, 22 of the spec: admin add/edit/delete/search/filter for individual Question Bank
// questions, plus the dashboard stat tiles. Subject/Topic *management* (create/retire) and Bulk
// Upload live on their own pages (linked from the header) to keep this one focused on questions.
export default function QuestionBankManagement() {
  const { token } = useAdminAuth();
  const navigate = useNavigate();
  const [mode, setMode] = useState("list"); // "list" | "form"
  const [editing, setEditing] = useState(null); // question being edited, or null for "add new"

  const [stats, setStats] = useState(null);
  const [subjects, setSubjects] = useState([]);

  const [items, setItems] = useState([]);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState("");
  const [subjectFilter, setSubjectFilter] = useState("");
  const [languageFilter, setLanguageFilter] = useState("");
  const [includeInactive, setIncludeInactive] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  useEffect(() => {
    getQuestionBankStats(token).then(setStats).catch(() => {});
    listSubjects(token, false).then(setSubjects).catch(() => {});
  }, [token]);

  useEffect(refresh, [token, page, subjectFilter, languageFilter, includeInactive]);

  function refresh() {
    setLoading(true);
    setError(null);
    listQuestionBankQuestions(token, { search, subjectId: subjectFilter, language: languageFilter, includeInactive, page, pageSize: PAGE_SIZE })
      .then((res) => {
        setItems(res.items);
        setTotalCount(res.totalCount);
      })
      .catch((err) => setError(friendlyError(err)))
      .finally(() => setLoading(false));
  }

  function handleSearchSubmit(e) {
    e.preventDefault();
    setPage(1);
    refresh();
  }

  async function handleDelete(q) {
    if (!window.confirm(`Remove "${q.questionText.slice(0, 60)}..." from the Question Bank?`)) return;
    try {
      await deleteQuestionBankQuestion(token, q.id);
      refresh();
    } catch (err) {
      window.alert(friendlyError(err));
    }
  }

  const totalPages = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));

  if (mode === "form") {
    return (
      <QuestionForm
        token={token}
        subjects={subjects}
        existing={editing}
        onCancel={() => {
          setMode("list");
          setEditing(null);
        }}
        onSaved={() => {
          setMode("list");
          setEditing(null);
          refresh();
          getQuestionBankStats(token).then(setStats).catch(() => {});
        }}
      />
    );
  }

  return (
    <div>
      <PageHeader
        title="Question Bank"
        subtitle="Individual, searchable PYQ questions — separate from the PYP paper upload flow"
        action={
          <div className="flex flex-wrap gap-2">
            <Button variant="secondary" onClick={() => navigate("/admin/question-bank/subjects-topics")}>
              <Tags className="h-4 w-4" strokeWidth={2.25} />
              Subjects &amp; Topics
            </Button>
            <Button variant="secondary" onClick={() => navigate("/admin/question-bank/reports")}>
              <Flag className="h-4 w-4" strokeWidth={2.25} />
              Reports
            </Button>
            <Button variant="secondary" onClick={() => navigate("/admin/question-bank/upload")}>
              <UploadCloud className="h-4 w-4" strokeWidth={2.25} />
              Bulk Upload
            </Button>
            <Button
              onClick={() => {
                setEditing(null);
                setMode("form");
              }}
            >
              <Plus className="h-4 w-4" strokeWidth={2.5} />
              Add Question
            </Button>
          </div>
        }
      />

      <div className="p-6">
        {stats && (
          <div className="mb-5 grid grid-cols-2 gap-3 sm:grid-cols-4 lg:grid-cols-7">
            <StatTile label="Questions" value={stats.totalQuestions} />
            <StatTile label="Subjects" value={stats.totalSubjects} />
            <StatTile label="Topics" value={stats.totalTopics} />
            <StatTile label="Exams used" value={stats.totalExamsUsed} />
            <StatTile label="Added today" value={stats.questionsAddedToday} />
            <StatTile label="Pending reports" value={stats.pendingReports} highlight={stats.pendingReports > 0} />
            <StatTile label="Pending solutions" value={stats.pendingAlternativeSolutions} highlight={stats.pendingAlternativeSolutions > 0} />
          </div>
        )}

        <Card>
          <form onSubmit={handleSearchSubmit} className="flex flex-wrap items-end gap-3">
            <div className="min-w-[220px] flex-1">
              <FormField label="Search">
                <TextInput value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Question text or keyword..." />
              </FormField>
            </div>
            <div className="w-52">
              <FormField label="Subject">
                <Select value={subjectFilter} onChange={(e) => { setSubjectFilter(e.target.value); setPage(1); }}>
                  <option value="">All subjects</option>
                  {subjects.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
                </Select>
              </FormField>
            </div>
            <div className="w-40">
              <FormField label="Language">
                <Select value={languageFilter} onChange={(e) => { setLanguageFilter(e.target.value); setPage(1); }}>
                  <option value="">All languages</option>
                  <option value="Hindi">Hindi</option>
                  <option value="English">English</option>
                </Select>
              </FormField>
            </div>
            <label className="flex items-center gap-1.5 pb-2.5 text-xs font-semibold text-ink-600">
              <input type="checkbox" checked={includeInactive} onChange={(e) => { setIncludeInactive(e.target.checked); setPage(1); }} />
              Show removed
            </label>
            <Button type="submit" variant="secondary">Search</Button>
          </form>
        </Card>

        {error && <div className="mt-4"><Alert>{error}</Alert></div>}

        <Card className="mt-4 !p-0">
          <div className="overflow-x-auto">
            <table className="w-full text-left text-xs">
              <thead className="bg-primary-50 text-ink-600">
                <tr>
                  <th className="px-3 py-2.5">Question</th>
                  <th className="px-3 py-2.5">Subject / Topic</th>
                  <th className="px-3 py-2.5">Language</th>
                  <th className="px-3 py-2.5">Asked In</th>
                  <th className="px-3 py-2.5">Methods</th>
                  <th className="px-3 py-2.5"></th>
                </tr>
              </thead>
              <tbody>
                {loading && (
                  <tr><td colSpan={6} className="px-3 py-6 text-center text-ink-400">Loading…</td></tr>
                )}
                {!loading && items.length === 0 && (
                  <tr><td colSpan={6} className="px-3 py-6 text-center text-ink-400">No questions found.</td></tr>
                )}
                {items.map((q) => (
                  <tr key={q.id} className={`border-t border-primary-50 ${q.isActive === false ? "opacity-50" : ""}`}>
                    <td className="max-w-sm px-3 py-2.5 align-top">
                      <span className="line-clamp-2 font-medium text-ink-900">{q.questionText}</span>
                    </td>
                    <td className="px-3 py-2.5 align-top text-ink-600">{q.subject} / {q.topic}</td>
                    <td className="px-3 py-2.5 align-top text-ink-600">{q.language || "—"}</td>
                    <td className="px-3 py-2.5 align-top text-ink-600">
                      {q.askedIn.slice(0, 2).map((a) => `${a.examName} ${a.year}`).join(", ")}
                      {q.askedIn.length > 2 ? ` +${q.askedIn.length - 2}` : ""}
                    </td>
                    <td className="px-3 py-2.5 align-top text-ink-600">{q.solutionCount}</td>
                    <td className="px-3 py-2.5 align-top">
                      <div className="flex items-center justify-end gap-2">
                        <button
                          type="button"
                          onClick={() => {
                            setEditing(q);
                            setMode("form");
                          }}
                          className="flex items-center gap-1 font-semibold text-secondary-500 hover:underline"
                        >
                          <Pencil className="h-3.5 w-3.5" strokeWidth={2.25} />
                          Edit
                        </button>
                        <button
                          type="button"
                          onClick={() => handleDelete(q)}
                          className="flex items-center gap-1 font-semibold text-red-500 hover:underline"
                        >
                          <Trash2 className="h-3.5 w-3.5" strokeWidth={2.25} />
                          Remove
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {totalPages > 1 && (
            <div className="flex items-center justify-center gap-3 border-t border-primary-50 py-3">
              <button type="button" disabled={page <= 1} onClick={() => setPage((p) => p - 1)} className="flex h-8 w-8 items-center justify-center rounded-lg border border-primary-100 text-primary-600 disabled:opacity-40">
                <ChevronLeft className="h-4 w-4" strokeWidth={2.5} />
              </button>
              <span className="text-xs font-medium text-ink-600">Page {page} of {totalPages} · {totalCount} total</span>
              <button type="button" disabled={page >= totalPages} onClick={() => setPage((p) => p + 1)} className="flex h-8 w-8 items-center justify-center rounded-lg border border-primary-100 text-primary-600 disabled:opacity-40">
                <ChevronRight className="h-4 w-4" strokeWidth={2.5} />
              </button>
            </div>
          )}
        </Card>
      </div>
    </div>
  );
}

function StatTile({ label, value, highlight }) {
  return (
    <div className={`rounded-xl2 p-3 shadow-card ${highlight ? "bg-accent-50" : "bg-white"}`}>
      <p className={`text-lg font-extrabold ${highlight ? "text-accent-600" : "text-ink-900"}`}>{value ?? "—"}</p>
      <p className="text-[11px] font-medium text-ink-400">{label}</p>
    </div>
  );
}

const EMPTY_EXAM_YEAR = { examId: "", examName: "", year: new Date().getFullYear() };

function QuestionForm({ token, subjects, existing, onCancel, onSaved }) {
  const isEdit = Boolean(existing);
  const [questionText, setQuestionText] = useState(existing?.questionText || "");
  const [options, setOptions] = useState({
    A: existing?.optionA || "", B: existing?.optionB || "", C: existing?.optionC || "", D: existing?.optionD || "",
  });
  const [correctOption, setCorrectOption] = useState(existing?.correctOption || "A");
  const [explanation, setExplanation] = useState(existing?.explanation || "");
  const [language, setLanguage] = useState(existing?.language || "");
  const [subjectId, setSubjectId] = useState(existing?.subjectId || "");
  const [topics, setTopics] = useState([]);
  const [topicId, setTopicId] = useState(existing?.topicId || "");
  const [sourceReference, setSourceReference] = useState(existing?.sourceReference || "");
  const [examYears, setExamYears] = useState(
    existing?.askedIn?.length ? existing.askedIn.map((a) => ({ examId: a.examId, examName: a.examName, year: a.year })) : [{ ...EMPTY_EXAM_YEAR }]
  );
  const [exams, setExams] = useState([]);

  const [images, setImages] = useState({}); // { questionImage, optionAImage, ... }: File|null
  const [removeFlags, setRemoveFlags] = useState({}); // { removeQuestionImage, ... }: bool

  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState(null);
  const [duplicate, setDuplicate] = useState(null); // { existingQuestionId, existingQuestionText }

  useEffect(() => {
    listExams().then(setExams).catch(() => {});
  }, []);

  useEffect(() => {
    if (!subjectId) {
      setTopics([]);
      return;
    }
    listTopics(token, { subjectId, includeInactive: false }).then(setTopics).catch(() => setTopics([]));
  }, [subjectId, token]);

  function updateExamYear(index, field, value) {
    setExamYears((prev) => prev.map((ey, i) => (i === index ? { ...ey, [field]: value } : ey)));
  }
  function addExamYear() {
    setExamYears((prev) => [...prev, { ...EMPTY_EXAM_YEAR }]);
  }
  function removeExamYear(index) {
    setExamYears((prev) => prev.filter((_, i) => i !== index));
  }

  function buildPayload(confirmDuplicate) {
    return {
      questionText: questionText.trim(),
      optionA: options.A.trim(),
      optionB: options.B.trim(),
      optionC: options.C.trim(),
      optionD: options.D.trim(),
      correctOption,
      explanation: explanation.trim() || null,
      subjectId,
      topicId,
      sourceReference: sourceReference.trim() || null,
      language: language || null,
      examYears: examYears
        .filter((ey) => ey.examId || ey.examName)
        .map((ey) => ({ examId: ey.examId || null, examName: ey.examId ? null : ey.examName, year: Number(ey.year) })),
      confirmCreateDespiteDuplicate: confirmDuplicate,
    };
  }

  async function handleSubmit(e, confirmDuplicate = false) {
    e?.preventDefault?.();
    setSubmitting(true);
    setError(null);
    if (!confirmDuplicate) setDuplicate(null);
    try {
      const payload = buildPayload(confirmDuplicate);
      let questionId = existing?.id;
      if (isEdit) {
        await updateQuestionBankQuestion(token, existing.id, payload);
      } else {
        const created = await createQuestionBankQuestion(token, payload);
        questionId = created.id;
      }

      const hasImageChanges = Object.values(images).some(Boolean) || Object.values(removeFlags).some(Boolean);
      if (hasImageChanges) {
        await uploadQuestionBankImages(token, questionId, images, removeFlags);
      }

      onSaved();
    } catch (err) {
      if (err.status === 409 && err.data?.existingQuestionId) {
        setDuplicate(err.data);
      } else {
        setError(friendlyError(err));
      }
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div>
      <PageHeader
        title={isEdit ? "Edit Question" : "Add Question"}
        action={<Button variant="ghost" onClick={onCancel}><X className="h-4 w-4" strokeWidth={2.5} />Cancel</Button>}
      />

      <div className="p-6">
        <Card className="max-w-3xl">
          {duplicate && (
            <div className="mb-4">
              <Alert>
                <div className="flex flex-col gap-2">
                  <span className="flex items-center gap-1.5 font-bold"><AlertTriangle className="h-4 w-4" strokeWidth={2.25} />Duplicate Question Found</span>
                  <span>A very similar question already exists: "{duplicate.existingQuestionText.slice(0, 140)}…"</span>
                  <span className="flex gap-2">
                    <Button variant="secondary" onClick={(e) => handleSubmit(e, true)} isLoading={submitting}>Create anyway</Button>
                    <Button variant="ghost" onClick={() => setDuplicate(null)}>Cancel</Button>
                  </span>
                </div>
              </Alert>
            </div>
          )}

          <form onSubmit={(e) => handleSubmit(e, false)} className="flex flex-col gap-3">
            <FormField label="Question text">
              <TextArea required rows={3} value={questionText} onChange={(e) => setQuestionText(e.target.value)} />
            </FormField>
            {isEdit ? (
              <EditImageField
                label="Question image (optional)"
                currentUrl={existing.questionImageUrl}
                onReplace={(f) => setImages((prev) => ({ ...prev, questionImage: f }))}
                onRemove={(v) => setRemoveFlags((prev) => ({ ...prev, removeQuestionImage: v }))}
              />
            ) : (
              <ImagePickerField label="Question image (optional)" file={images.questionImage} onChange={(f) => setImages((prev) => ({ ...prev, questionImage: f }))} />
            )}

            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
              {["A", "B", "C", "D"].map((letter) => (
                <FormField key={letter} label={`Option ${letter}`}>
                  <div className="flex items-center gap-2">
                    <input
                      type="radio"
                      name="correctOption"
                      checked={correctOption === letter}
                      onChange={() => setCorrectOption(letter)}
                      className="h-4 w-4 accent-mint-500"
                      title={`Mark ${letter} as correct`}
                    />
                    <TextInput required value={options[letter]} onChange={(e) => setOptions((prev) => ({ ...prev, [letter]: e.target.value }))} />
                  </div>
                  {isEdit ? (
                    <EditImageField
                      label={`Option ${letter} image`}
                      currentUrl={existing[`option${letter}ImageUrl`]}
                      onReplace={(f) => setImages((prev) => ({ ...prev, [`option${letter}Image`]: f }))}
                      onRemove={(v) => setRemoveFlags((prev) => ({ ...prev, [`removeOption${letter}Image`]: v }))}
                      compact
                    />
                  ) : (
                    <ImagePickerField label={`Option ${letter} image`} file={images[`option${letter}Image`]} onChange={(f) => setImages((prev) => ({ ...prev, [`option${letter}Image`]: f }))} compact />
                  )}
                </FormField>
              ))}
            </div>
            <p className="text-xs text-ink-400">Select the radio button next to the correct option.</p>

            <FormField label="Explanation (optional)">
              <TextArea rows={3} value={explanation} onChange={(e) => setExplanation(e.target.value)} />
            </FormField>
            {isEdit ? (
              <EditImageField
                label="Explanation image (optional)"
                currentUrl={existing.explanationImageUrl}
                onReplace={(f) => setImages((prev) => ({ ...prev, explanationImage: f }))}
                onRemove={(v) => setRemoveFlags((prev) => ({ ...prev, removeExplanationImage: v }))}
              />
            ) : (
              <ImagePickerField label="Explanation image (optional)" file={images.explanationImage} onChange={(f) => setImages((prev) => ({ ...prev, explanationImage: f }))} />
            )}

            <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
              <FormField label="Subject">
                <Select required value={subjectId} onChange={(e) => { setSubjectId(e.target.value); setTopicId(""); }}>
                  <option value="">Select subject</option>
                  {subjects.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
                </Select>
              </FormField>
              <FormField label="Topic" hint={!subjectId ? "Select a Subject first" : undefined}>
                <Select required disabled={!subjectId} value={topicId} onChange={(e) => setTopicId(e.target.value)}>
                  <option value="">Select topic</option>
                  {topics.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}
                </Select>
              </FormField>
              <FormField label="Language" hint="Which language this question is written in">
                <Select value={language} onChange={(e) => setLanguage(e.target.value)}>
                  <option value="">Not specified</option>
                  <option value="Hindi">Hindi</option>
                  <option value="English">English</option>
                </Select>
              </FormField>
            </div>

            <FormField label="Source reference (optional)" hint="e.g. NCERT Class 11, Ch. 4 -- not linked to a Paper upload, just a citation">
              <TextInput value={sourceReference} onChange={(e) => setSourceReference(e.target.value)} />
            </FormField>

            <div>
              <span className="mb-1 block text-xs font-semibold text-ink-600">
                Appeared in (Exam + Year) — a question can appear in more than one
              </span>
              <div className="flex flex-col gap-2">
                {examYears.map((ey, i) => (
                  <div key={i} className="flex items-center gap-2">
                    <Select
                      value={ey.examId}
                      onChange={(e) => updateExamYear(i, "examId", e.target.value)}
                      className="flex-1"
                    >
                      <option value="">— type a new exam name instead —</option>
                      {exams.map((ex) => <option key={ex.id} value={ex.id}>{ex.name}</option>)}
                    </Select>
                    {!ey.examId && (
                      <TextInput
                        placeholder="New exam name"
                        value={ey.examName}
                        onChange={(e) => updateExamYear(i, "examName", e.target.value)}
                        className="flex-1"
                      />
                    )}
                    <TextInput
                      type="number"
                      value={ey.year}
                      onChange={(e) => updateExamYear(i, "year", e.target.value)}
                      className="w-24"
                    />
                    {examYears.length > 1 && (
                      <button type="button" onClick={() => removeExamYear(i)} className="text-ink-400 hover:text-red-500">
                        <X className="h-4 w-4" strokeWidth={2.5} />
                      </button>
                    )}
                  </div>
                ))}
              </div>
              <button type="button" onClick={addExamYear} className="mt-2 flex items-center gap-1 text-xs font-semibold text-secondary-500 hover:underline">
                <Plus className="h-3.5 w-3.5" strokeWidth={2.5} />
                Add another exam/year
              </button>
            </div>

            {error && <Alert>{error}</Alert>}

            <div className="mt-2 flex gap-2">
              <Button type="submit" isLoading={submitting}>
                {isEdit ? "Save changes" : "Add question"}
              </Button>
              <Button type="button" variant="ghost" onClick={onCancel}>Cancel</Button>
            </div>
          </form>
        </Card>
      </div>
    </div>
  );
}
