import { useEffect, useRef, useState } from "react";
import { CheckCircle2, ImagePlus, Plus, PlusCircle, ArrowLeft, ArrowRight, X, Layers, PenLine, Link2Off } from "lucide-react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { useAdminAuth } from "../context/AdminAuthContext";
import { listExams, createExam } from "../api/exams";
import { createOrFindPaper, getPaper, submitPaper, listPapers, getMappedQuestions, updatePaperConfig, mapQuestionToPaper, mapQuestionsBulkToPaper, unmapQuestionFromPaper, validatePaper } from "../api/papers";
import { createQuestion } from "../api/adminQuestions";
import BulkImportPanel from "../components/BulkImportPanel";
import TestQuestionPicker from "../components/TestQuestionPicker";
import PaperQuestionBulkPicker from "../components/PaperQuestionBulkPicker";
import { PracticeSettingsCard, ValidationSummary } from "../components/PaperConfigAndValidation";
import { API_BASE_URL } from "../../api/client";
import { PageHeader, Card, Button, FormField, TextInput, TextArea, Select, Alert, StatusBadge, friendlyError } from "../components/AdminUI";

const LANGUAGES = ["Hindi", "English"];
const DIFFICULTIES = ["Easy", "Medium", "Hard"];

const EMPTY_QUESTION_TEXT_FIELDS = {
  subject: "",
  topic: "",
  questionText: "",
  optionA: "",
  optionB: "",
  optionC: "",
  optionD: "",
  correctOption: "A",
  difficultyLevel: "Medium",
  explanation: "",
  sourceReference: "",
};

function logoSrc(logoUrl) {
  if (!logoUrl) return null;
  return logoUrl.startsWith("http") ? logoUrl : `${API_BASE_URL}${logoUrl}`;
}

export default function PyqUploadWizard() {
  const { token } = useAdminAuth();
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const resumePaperId = searchParams.get("resume");

  const [step, setStep] = useState(1); // 1: exam, 2: language, 3: paper details, 4: questions
  const [loadingResume, setLoadingResume] = useState(Boolean(resumePaperId));

  const [exams, setExams] = useState([]);
  const [examsLoading, setExamsLoading] = useState(true);
  const [selectedExam, setSelectedExam] = useState(null);
  const [language, setLanguage] = useState("");

  const [year, setYear] = useState(new Date().getFullYear());
  const [paperCode, setPaperCode] = useState("");
  const [tier, setTier] = useState("");
  const [examDate, setExamDate] = useState("");
  const [shift, setShift] = useState("");
  const [paperLabel, setPaperLabel] = useState("");
  const [paper, setPaper] = useState(null);

  const [nextQuestionNumber, setNextQuestionNumber] = useState(1);
  const [lastSubject, setLastSubject] = useState("");
  const [uploadedCount, setUploadedCount] = useState(0);

  useEffect(() => {
    refreshExams();
  }, []);

  // Resume flow: jump straight to step 4 with the paper's context already loaded.
  useEffect(() => {
    if (!resumePaperId) return;
    let cancelled = false;

    getPaper(token, resumePaperId)
      .then(({ paper: p, questions }) => {
        if (cancelled) return;
        setSelectedExam({ id: p.examId, name: p.examName, logoUrl: p.examLogoUrl });
        setLanguage(p.language);
        setYear(p.year);
        setPaperCode(p.paperCode || "");
        setTier(p.tier || "");
        setExamDate(p.examDate || "");
        setShift(p.shift || "");
        setPaperLabel(p.paperLabel || "");
        setPaper(p);
        const last = questions[questions.length - 1];
        setNextQuestionNumber(last ? last.questionNumber + 1 : 1);
        setLastSubject(last ? last.subject : "");
        setUploadedCount(questions.length);
        setStep(4);
      })
      .catch(() => {
        // Paper may have been deleted since the list was loaded -- fall back to a fresh wizard.
      })
      .finally(() => {
        if (!cancelled) {
          setLoadingResume(false);
          setSearchParams((prev) => {
            const next = new URLSearchParams(prev);
            next.delete("resume");
            return next;
          }, { replace: true });
        }
      });

    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [resumePaperId]);

  function refreshExams() {
    setExamsLoading(true);
    listExams()
      .then(setExams)
      .catch(() => setExams([]))
      .finally(() => setExamsLoading(false));
  }

  if (loadingResume) {
    return (
      <div>
        <PageHeader title="PYP Paper Builder" subtitle="Loading paper…" />
        <div className="p-6 text-sm text-ink-400">Loading…</div>
      </div>
    );
  }

  return (
    <div>
      <PageHeader
        title="PYP Paper Builder"
        subtitle="Step 1: exam  →  Step 2: language  →  Step 3: paper identity  →  Step 4: questions (new + Question Bank)"
      />

      <div className="p-6">
        <Stepper step={step} />

        <div className="mt-6">
          {step === 1 && (
            <ExamStep
              exams={exams}
              isLoading={examsLoading}
              onSelect={(exam) => {
                setSelectedExam(exam);
                setStep(2);
              }}
              onExamCreated={(exam) => {
                setExams((prev) => [...prev, exam].sort((a, b) => a.name.localeCompare(b.name)));
                setSelectedExam(exam);
                setStep(2);
              }}
              token={token}
            />
          )}

          {step === 2 && (
            <LanguageStep
              exam={selectedExam}
              language={language}
              onChangeLanguage={setLanguage}
              onBack={() => setStep(1)}
              onNext={() => setStep(3)}
            />
          )}

          {step === 3 && (
            <PaperDetailsStep
              exam={selectedExam}
              language={language}
              year={year}
              paperCode={paperCode}
              tier={tier}
              examDate={examDate}
              shift={shift}
              paperLabel={paperLabel}
              onChangeYear={setYear}
              onChangePaperCode={setPaperCode}
              onChangeTier={setTier}
              onChangeExamDate={setExamDate}
              onChangeShift={setShift}
              onChangePaperLabel={setPaperLabel}
              onResumeExisting={(existingPaper, questions) => {
                setPaper(existingPaper);
                const last = questions[questions.length - 1];
                setNextQuestionNumber(last ? last.questionNumber + 1 : 1);
                setLastSubject(last ? last.subject : "");
                setUploadedCount(questions.length);
                setStep(4);
              }}
              onCreated={(newPaper) => {
                setPaper(newPaper);
                setNextQuestionNumber(1);
                setLastSubject("");
                setUploadedCount(0);
                setStep(4);
              }}
              onBack={() => setStep(2)}
              token={token}
            />
          )}

          {step === 4 && paper && (
            <QuestionStep
              exam={selectedExam}
              language={language}
              paper={paper}
              nextQuestionNumber={nextQuestionNumber}
              lastSubject={lastSubject}
              uploadedCount={uploadedCount}
              onQuestionUploaded={(subject, questionNumber) => {
                setLastSubject(subject);
                setNextQuestionNumber(questionNumber + 1);
                setUploadedCount((c) => c + 1);
              }}
              onBulkImported={async () => {
                const { questions } = await getPaper(token, paper.id);
                setUploadedCount(questions.length);
                const maxNumber = questions.reduce((max, q) => Math.max(max, q.questionNumber || 0), 0);
                setNextQuestionNumber(maxNumber + 1);
              }}
              onChangeExamOrLanguage={() => setStep(1)}
              onDone={() => navigate("/admin/papers")}
              token={token}
            />
          )}
        </div>
      </div>
    </div>
  );
}

function Stepper({ step }) {
  const steps = ["Exam", "Language", "Paper", "Questions"];
  return (
    <div className="flex flex-wrap items-center gap-2">
      {steps.map((label, i) => {
        const n = i + 1;
        const isActive = n === step;
        const isDone = n < step;
        return (
          <div key={label} className="flex items-center gap-2">
            <span
              className={`flex h-7 w-7 items-center justify-center rounded-full text-xs font-bold ${
                isDone ? "bg-mint-500 text-white" : isActive ? "bg-primary-600 text-white" : "bg-primary-50 text-ink-400"
              }`}
            >
              {isDone ? <CheckCircle2 className="h-4 w-4" strokeWidth={2.5} /> : n}
            </span>
            <span className={`text-sm font-semibold ${isActive ? "text-ink-900" : "text-ink-400"}`}>{label}</span>
            {n < steps.length && <span className="mx-2 h-px w-8 bg-primary-100" />}
          </div>
        );
      })}
    </div>
  );
}

// ---------- Step 1: choose or create exam ----------
function ExamStep({ exams, isLoading, onSelect, onExamCreated, token }) {
  const [showNewExamForm, setShowNewExamForm] = useState(false);
  const [name, setName] = useState("");
  const [logoFile, setLogoFile] = useState(null);
  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState(null);

  async function handleCreate(e) {
    e.preventDefault();
    setCreating(true);
    setCreateError(null);
    try {
      const exam = await createExam(token, { name: name.trim(), logoFile });
      setName("");
      setLogoFile(null);
      setShowNewExamForm(false);
      onExamCreated(exam);
    } catch (err) {
      setCreateError(err.message);
    } finally {
      setCreating(false);
    }
  }

  return (
    <div>
      <h2 className="text-sm font-bold text-ink-900">Choose the exam you're uploading questions for</h2>

      {isLoading ? (
        <p className="mt-4 text-sm text-ink-400">Loading exams…</p>
      ) : (
        <div className="mt-4 grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
          {exams.map((exam) => (
            <button key={exam.id} type="button" onClick={() => onSelect(exam)} className="text-left">
              <Card className="flex h-full flex-col items-center gap-2 text-center transition-shadow hover:shadow-cardHover">
                {logoSrc(exam.logoUrl) ? (
                  <img src={logoSrc(exam.logoUrl)} alt="" className="h-12 w-12 rounded-lg object-cover" />
                ) : (
                  <span className="flex h-12 w-12 items-center justify-center rounded-lg bg-primary-50 text-sm font-bold text-primary-600">
                    {exam.name.slice(0, 2).toUpperCase()}
                  </span>
                )}
                <span className="text-sm font-bold text-ink-900">{exam.name}</span>
                <span className="text-xs text-ink-400">{exam.questionCount} questions</span>
              </Card>
            </button>
          ))}

          <button type="button" onClick={() => setShowNewExamForm(true)} className="text-left">
            <Card className="flex h-full flex-col items-center justify-center gap-2 border-2 border-dashed border-primary-100 text-center shadow-none hover:border-secondary-500">
              <Plus className="h-6 w-6 text-secondary-500" strokeWidth={2.25} />
              <span className="text-sm font-bold text-secondary-500">New Exam</span>
            </Card>
          </button>
        </div>
      )}

      {showNewExamForm && (
        <div className="mt-6 max-w-md">
          <Card>
            <h3 className="text-sm font-bold text-ink-900">Create a new exam</h3>
            <form onSubmit={handleCreate} className="mt-4 flex flex-col gap-3">
              <FormField label="Exam name">
                <TextInput
                  required
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  placeholder="e.g. SSC CHSL"
                  autoFocus
                />
              </FormField>

              <FormField label="Exam logo" hint="Optional. PNG/JPG/WEBP/SVG, up to 2 MB.">
                <label className="flex cursor-pointer items-center gap-2.5 rounded-xl2 border border-dashed border-primary-100 bg-white px-3.5 py-2.5 text-sm text-ink-600 hover:border-secondary-500">
                  <ImagePlus className="h-4 w-4 text-ink-400" strokeWidth={2} />
                  {logoFile ? logoFile.name : "Choose a logo file"}
                  <input
                    type="file"
                    accept=".png,.jpg,.jpeg,.webp,.svg"
                    className="hidden"
                    onChange={(e) => setLogoFile(e.target.files?.[0] || null)}
                  />
                </label>
              </FormField>

              {createError && <Alert>{createError}</Alert>}

              <div className="flex gap-2">
                <Button type="submit" isLoading={creating}>
                  Create exam
                </Button>
                <Button type="button" variant="ghost" onClick={() => setShowNewExamForm(false)}>
                  Cancel
                </Button>
              </div>
            </form>
          </Card>
        </div>
      )}
    </div>
  );
}

// ---------- Step 2: choose language ----------
function LanguageStep({ exam, language, onChangeLanguage, onBack, onNext }) {
  return (
    <div className="max-w-md">
      <h2 className="text-sm font-bold text-ink-900">
        What language are these <span className="text-primary-600">{exam?.name}</span> questions in?
      </h2>

      <div className="mt-4 flex gap-3">
        {LANGUAGES.map((lang) => (
          <button
            key={lang}
            type="button"
            onClick={() => onChangeLanguage(lang)}
            className={`flex-1 rounded-xl2 border-2 px-4 py-4 text-sm font-bold transition-colors ${
              language === lang
                ? "border-primary-600 bg-primary-50 text-primary-700"
                : "border-primary-100 text-ink-600 hover:border-primary-300"
            }`}
          >
            {lang}
          </button>
        ))}
      </div>

      <div className="mt-5 flex gap-2">
        <Button variant="ghost" onClick={onBack}>
          <ArrowLeft className="h-4 w-4" strokeWidth={2.5} />
          Back
        </Button>
        <Button onClick={onNext} disabled={!language}>
          Continue
          <ArrowRight className="h-4 w-4" strokeWidth={2.5} />
        </Button>
      </div>
    </div>
  );
}

// ---------- Step 3: year / paper code (defines the Paper) ----------
function PaperDetailsStep({
  exam, language, year, paperCode, tier, examDate, shift, paperLabel,
  onChangeYear, onChangePaperCode, onChangeTier, onChangeExamDate, onChangeShift, onChangePaperLabel,
  onResumeExisting, onCreated, onBack, token,
}) {
  const [checked, setChecked] = useState(false);
  const [checking, setChecking] = useState(false);
  const [existingPapers, setExistingPapers] = useState([]);
  const [showNewSetForm, setShowNewSetForm] = useState(false);
  const [creating, setCreating] = useState(false);
  const [resumingId, setResumingId] = useState(null);
  const [error, setError] = useState(null);

  async function handleCheck(e) {
    e.preventDefault();
    setChecking(true);
    setError(null);
    try {
      // Tier/Date/Shift/PaperLabel are part of a paper's full identity now (see Models/Paper.cs) --
      // included here so two different shifts of the same Exam+Year+Language don't get confused
      // with each other in the "already exists" list below.
      const res = await listPapers(token, {
        examId: exam.id, year: Number(year), language,
        tier: tier || undefined, examDate: examDate || undefined,
        shift: shift || undefined, paperLabel: paperLabel || undefined,
      });
      setExistingPapers(res.items);
      setShowNewSetForm(res.items.length === 0); // nothing exists yet -- skip straight to creating
      setChecked(true);
    } catch (err) {
      setError(friendlyError(err));
    } finally {
      setChecking(false);
    }
  }

  async function handleUseExisting(paperId) {
    setResumingId(paperId);
    setError(null);
    try {
      const { paper, questions } = await getPaper(token, paperId);
      onResumeExisting(paper, questions);
    } catch (err) {
      setError(friendlyError(err));
    } finally {
      setResumingId(null);
    }
  }

  async function handleCreateNewSet(e) {
    e.preventDefault();
    setCreating(true);
    setError(null);
    try {
      const created = await createOrFindPaper(token, {
        examId: exam.id,
        year: Number(year),
        language,
        paperCode,
        tier,
        examDate: examDate || null,
        shift,
        paperLabel,
      });
      onCreated(created);
    } catch (err) {
      if (err.status === 409 && err.data) {
        // Someone else created the exact same identity in the moment between our check and this
        // submit -- resume that instead of erroring out.
        handleUseExisting(err.data.id);
      } else {
        setError(friendlyError(err));
      }
    } finally {
      setCreating(false);
    }
  }

  function handleChangeIdentity(setter) {
    return (value) => {
      setter(value);
      setChecked(false);
      setExistingPapers([]);
      setShowNewSetForm(false);
    };
  }

  return (
    <div className="max-w-md">
      <h2 className="text-sm font-bold text-ink-900">
        <span className="text-primary-600">{exam?.name}</span> · {language} — which paper is this?
      </h2>

      <form onSubmit={checked ? undefined : handleCheck} className="mt-4 flex flex-col gap-3">
        <FormField label="Year">
          <TextInput required type="number" value={year} onChange={(e) => handleChangeIdentity(onChangeYear)(e.target.value)} />
        </FormField>

        {/* Tier/Date/Shift/Paper-label are all optional -- most exams don't need any of them, so
            leave blank and this behaves exactly like before (Exam+Year+Language only). */}
        <div className="grid grid-cols-2 gap-3">
          <FormField label="Tier (optional)" hint="e.g. Tier 1">
            <TextInput value={tier} onChange={(e) => handleChangeIdentity(onChangeTier)(e.target.value)} placeholder="Tier 1" />
          </FormField>
          <FormField label="Shift (optional)" hint="e.g. Shift 1">
            <TextInput value={shift} onChange={(e) => handleChangeIdentity(onChangeShift)(e.target.value)} placeholder="Shift 1" />
          </FormField>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <FormField label="Exam date (optional)">
            <TextInput type="date" value={examDate} onChange={(e) => handleChangeIdentity(onChangeExamDate)(e.target.value)} />
          </FormField>
          <FormField label="Paper label (optional)" hint="e.g. General Awareness">
            <TextInput value={paperLabel} onChange={(e) => handleChangeIdentity(onChangePaperLabel)(e.target.value)} placeholder="General Awareness" />
          </FormField>
        </div>

        {error && <Alert>{error}</Alert>}

        {!checked && (
          <div className="mt-2 flex gap-2">
            <Button type="button" variant="ghost" onClick={onBack}>
              <ArrowLeft className="h-4 w-4" strokeWidth={2.5} />
              Back
            </Button>
            <Button type="submit" isLoading={checking}>
              Check
              <ArrowRight className="h-4 w-4" strokeWidth={2.5} />
            </Button>
          </div>
        )}
      </form>

      {checked && existingPapers.length > 0 && (
        <div className="mt-4">
          <p className="text-xs font-semibold text-ink-600">
            {existingPapers.length} paper{existingPapers.length > 1 ? "s" : ""} already exist{existingPapers.length > 1 ? "" : "s"} with this exact identity:
          </p>
          <div className="mt-2 flex flex-col gap-2">
            {existingPapers.map((p) => (
              <Card key={p.id} className="flex items-center justify-between gap-3">
                <span className="text-sm">
                  <span className="font-bold text-ink-900">{p.paperCode || "No code / default set"}</span>
                  <span className="ml-2 text-xs text-ink-400">
                    {p.status} · {p.questionCount} question{p.questionCount === 1 ? "" : "s"}
                  </span>
                </span>
                <Button isLoading={resumingId === p.id} onClick={() => handleUseExisting(p.id)}>
                  Use this paper
                </Button>
              </Card>
            ))}
          </div>

          {!showNewSetForm && (
            <button
              type="button"
              onClick={() => setShowNewSetForm(true)}
              className="mt-3 text-xs font-semibold text-secondary-500 hover:underline"
            >
              None of these — this is a different Set with its own code
            </button>
          )}
        </div>
      )}

      {checked && showNewSetForm && (
        <form onSubmit={handleCreateNewSet} className="mt-4 flex flex-col gap-3 border-t border-primary-100 pt-4">
          <FormField
            label={existingPapers.length > 0 ? "New Paper Code" : "Paper Code (optional)"}
            hint={
              existingPapers.length > 0
                ? "Required here, since at least one paper already exists for this exam/year/language -- give this Set a distinct code."
                : "Only needed if this year ends up having multiple question Sets (Set A / Set B / ...) later."
            }
          >
            <TextInput
              required={existingPapers.length > 0}
              value={paperCode}
              onChange={(e) => onChangePaperCode(e.target.value)}
              placeholder="e.g. Set A"
              autoFocus
            />
          </FormField>

          <div className="flex gap-2">
            {existingPapers.length === 0 && (
              <Button type="button" variant="ghost" onClick={() => setChecked(false)}>
                <ArrowLeft className="h-4 w-4" strokeWidth={2.5} />
                Back
              </Button>
            )}
            <Button type="submit" isLoading={creating}>
              Create this paper
              <ArrowRight className="h-4 w-4" strokeWidth={2.5} />
            </Button>
          </div>
        </form>
      )}
    </div>
  );
}

// ---------- Step 4: question loop ----------
function QuestionStep({
  exam, language, paper, nextQuestionNumber, lastSubject, uploadedCount,
  onQuestionUploaded, onBulkImported, onChangeExamOrLanguage, onDone, token,
}) {
  const navigate = useNavigate();
  const [mode, setMode] = useState("single"); // "single" | "bulk"
  const [fields, setFields] = useState({ ...EMPTY_QUESTION_TEXT_FIELDS, subject: lastSubject });
  const [images, setImages] = useState({});
  const [questionNumber, setQuestionNumber] = useState(nextQuestionNumber);
  const [submitting, setSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState(null);
  const [finishing, setFinishing] = useState(false);
  const [finishError, setFinishError] = useState(null);
  const [finishResult, setFinishResult] = useState(null);

  // ---------- Question Bank side (spec section 12/16: same paper, either source) ----------
  const [mappedQuestions, setMappedQuestions] = useState(null); // full merged list, tagged by source
  const [validation, setValidation] = useState(null);
  const [configSaving, setConfigSaving] = useState(false);
  const [showQBPicker, setShowQBPicker] = useState(false);
  const [showBulkQBPicker, setShowBulkQBPicker] = useState(false);
  const [mappingError, setMappingError] = useState(null);
  const [refreshKey, setRefreshKey] = useState(0);

  useEffect(() => {
    let cancelled = false;
    Promise.all([getMappedQuestions(token, paper.id), validatePaper(token, paper.id)])
      .then(([mapped, validationResult]) => {
        if (cancelled) return;
        setMappedQuestions(mapped);
        setValidation(validationResult);
      })
      .catch(() => {});
    return () => { cancelled = true; };
  }, [paper.id, refreshKey, token]);

  function refreshMapped() {
    setRefreshKey((k) => k + 1);
  }

  async function handleSaveConfig(config) {
    setConfigSaving(true);
    setMappingError(null);
    try {
      await updatePaperConfig(token, paper.id, config);
      refreshMapped();
    } catch (err) {
      setMappingError(friendlyError(err));
    } finally {
      setConfigSaving(false);
    }
  }

  async function handleMapQuestion({ questionBankQuestionId }) {
    setMappingError(null);
    // Auto-assign the next free question number -- same "no confusion" default as PaperDetailView.
    const usedNumbers = (mappedQuestions || []).map((m) => m.questionNumber);
    const nextNumber = usedNumbers.length > 0 ? Math.max(...usedNumbers) + 1 : 1;
    try {
      await mapQuestionToPaper(token, paper.id, { questionBankQuestionId, questionNumber: nextNumber });
      refreshMapped();
    } catch (err) {
      setMappingError(friendlyError(err));
    }
  }

  async function handleBulkAdd(questionBankQuestionIds) {
    const result = await mapQuestionsBulkToPaper(token, paper.id, questionBankQuestionIds);
    refreshMapped();
    return result;
  }

  async function handleUnmapQuestion(linkId) {
    setMappingError(null);
    try {
      await unmapQuestionFromPaper(token, paper.id, linkId);
      refreshMapped();
    } catch (err) {
      setMappingError(friendlyError(err));
    }
  }

  useEffect(() => {
    setQuestionNumber(nextQuestionNumber);
    setFields((f) => ({ ...EMPTY_QUESTION_TEXT_FIELDS, subject: lastSubject, difficultyLevel: f.difficultyLevel }));
    setImages({});
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [nextQuestionNumber]);

  function updateField(key, value) {
    setFields((f) => ({ ...f, [key]: value }));
  }

  function updateImage(key, file) {
    setImages((img) => ({ ...img, [key]: file }));
  }

  async function handleSubmitQuestion(e) {
    e.preventDefault();
    setSubmitting(true);
    setSubmitError(null);
    try {
      await createQuestion(
        token,
        { paperId: paper.id, questionNumber: Number(questionNumber), ...fields },
        images
      );
      onQuestionUploaded(fields.subject, Number(questionNumber));
      refreshMapped();
    } catch (err) {
      setSubmitError(friendlyError(err));
    } finally {
      setSubmitting(false);
    }
  }

  async function handleFinish() {
    setFinishing(true);
    setFinishError(null);
    try {
      const result = await submitPaper(token, paper.id);
      setFinishResult(result);
    } catch (err) {
      setFinishError(friendlyError(err));
    } finally {
      setFinishing(false);
    }
  }

  if (finishResult) {
    const published = finishResult.status === "Published";
    return (
      <div className="max-w-xl">
        <Alert type="success">
          {published
            ? `Paper published! ${finishResult.questionCount} questions are now live for students.`
            : `Paper submitted for review. ${finishResult.questionCount} questions are waiting on a Super Admin (or a Publish-permission admin) to approve.`}
        </Alert>
        <Button className="mt-4" onClick={onDone}>
          Go to Uploaded Papers
        </Button>
      </div>
    );
  }

  return (
    <div className="max-w-2xl">
      <div className="flex flex-wrap items-center gap-2 text-xs font-semibold text-ink-400">
        <span className="rounded-full bg-primary-50 px-2.5 py-1 text-primary-600">{exam?.name}</span>
        <span className="rounded-full bg-primary-50 px-2.5 py-1 text-primary-600">{language}</span>
        <span className="rounded-full bg-primary-50 px-2.5 py-1 text-primary-600">{paper.year}</span>
        {paper.paperCode && <span className="rounded-full bg-primary-50 px-2.5 py-1 text-primary-600">{paper.paperCode}</span>}
        <StatusBadge status={paper.status} />
        <button type="button" onClick={onChangeExamOrLanguage} className="font-semibold text-secondary-500 hover:underline">
          Change exam/language
        </button>
      </div>

      <div className="mt-4">
        <PracticeSettingsCard paper={paper} canEdit={paper.status === "Draft"} isLoading={configSaving} onSave={handleSaveConfig} />
      </div>

      {validation && (
        <div className="mb-4">
          <ValidationSummary validation={validation} />
        </div>
      )}
      {mappingError && <div className="mb-4"><Alert>{mappingError}</Alert></div>}

      {paper.status !== "Draft" ? (
        <div className="mt-4">
          <Alert>
            {paper.status === "Published"
              ? "This paper is already Published, so new questions can't be added here. Unpublish it from the paper's detail page first if you need to add more."
              : "This paper is already submitted for review, so new questions can't be added here. You can still fix an existing question's text, options, or images from the paper's detail page while it's Pending Review."}
          </Alert>
          <Button className="mt-3" variant="secondary" onClick={() => navigate(`/admin/papers/${paper.id}`)}>
            Go to paper details
          </Button>
        </div>
      ) : (
        <>
      {/* This paper's full question list so far, whichever source each one came from -- spec
          section 10, "Question Source Transparency". Students never see this distinction; only
          admins building the paper do. */}
      {mappedQuestions && mappedQuestions.length > 0 && (
        <Card className="mb-4">
          <h4 className="text-xs font-bold text-ink-900">Questions in this paper ({mappedQuestions.length})</h4>
          <div className="mt-2 flex max-h-64 flex-col gap-1.5 overflow-y-auto">
            {mappedQuestions.map((m) => (
              <div key={`${m.source}-${m.questionId}-${m.questionNumber}`} className="flex items-start gap-2 rounded-lg border border-primary-100 p-2 text-xs">
                <span className="mt-0.5 shrink-0 rounded-full bg-primary-50 px-2 py-0.5 font-bold text-primary-600">
                  Q.{m.questionNumber}{!m.isNumberExact && <span className="ml-1 font-normal text-accent-600" title="Auto-assigned, not the exact original position">~</span>}
                </span>
                <span className="min-w-0 flex-1">
                  <span className="line-clamp-1 text-ink-900">{m.questionText}</span>
                  <span className="mt-0.5 block text-[11px] text-ink-400">
                    {m.subject} / {m.topic} · {m.source === "QuestionBank" ? "Question Bank" : "typed directly"}
                  </span>
                </span>
                {m.source === "QuestionBank" && (
                  <button
                    type="button"
                    onClick={() => handleUnmapQuestion(m.linkId)}
                    className="shrink-0 rounded-lg p-1 text-ink-400 hover:bg-red-50 hover:text-red-600"
                    title="Unmap from this paper"
                  >
                    <Link2Off className="h-3.5 w-3.5" strokeWidth={2.25} />
                  </button>
                )}
              </div>
            ))}
          </div>
        </Card>
      )}

      <Card className="mb-4">
        <h4 className="text-xs font-bold text-ink-900">Add from Question Bank</h4>
        <p className="mt-0.5 text-xs text-ink-400">
          Reuse questions that already exist in the Question Bank instead of retyping them (spec
          section 2) -- pick one at an exact Q.No, or bulk-add everything already tagged for this
          exam/year.
        </p>

        {showQBPicker && (
          <div className="mt-3 border-t border-primary-100 pt-3">
            <h5 className="mb-2 text-xs font-bold text-ink-900">Add one, at an exact Q.No</h5>
            <TestQuestionPicker
              token={token}
              selectedRefs={(mappedQuestions || []).filter((m) => m.source === "QuestionBank").map((m) => ({ questionBankQuestionId: m.questionId }))}
              onAdd={handleMapQuestion}
            />
          </div>
        )}
        {showBulkQBPicker && (
          <div className="mt-3 border-t border-primary-100 pt-3">
            <h5 className="mb-2 text-xs font-bold text-ink-900">Bulk-add several at once</h5>
            <PaperQuestionBulkPicker
              token={token}
              examId={paper.examId}
              year={paper.year}
              mappedQuestionBankIds={(mappedQuestions || []).filter((m) => m.source === "QuestionBank").map((m) => m.questionId)}
              onBulkAdd={handleBulkAdd}
            />
          </div>
        )}
        {(showQBPicker || showBulkQBPicker) ? (
          <Button variant="ghost" className="mt-3" onClick={() => { setShowQBPicker(false); setShowBulkQBPicker(false); }}>Done</Button>
        ) : (
          <div className="mt-3 flex flex-wrap gap-2">
            <Button variant="secondary" onClick={() => setShowQBPicker(true)}>
              <PlusCircle className="h-4 w-4" strokeWidth={2.25} />
              Map one, at an exact Q.No
            </Button>
            <Button variant="secondary" onClick={() => setShowBulkQBPicker(true)}>
              <PlusCircle className="h-4 w-4" strokeWidth={2.25} />
              Bulk-add from Question Bank
            </Button>
          </div>
        )}
      </Card>

      <h4 className="mb-2 text-xs font-bold text-ink-900">Or type a new question directly</h4>

      <div className="mt-4 flex gap-2">
        <button
          type="button"
          onClick={() => setMode("single")}
          className={`flex items-center gap-1.5 rounded-xl2 px-3.5 py-2 text-xs font-semibold transition-colors ${
            mode === "single" ? "bg-primary-600 text-white" : "bg-primary-50 text-primary-600 hover:bg-primary-100"
          }`}
        >
          <PenLine className="h-3.5 w-3.5" strokeWidth={2.25} />
          One by one
        </button>
        <button
          type="button"
          onClick={() => setMode("bulk")}
          className={`flex items-center gap-1.5 rounded-xl2 px-3.5 py-2 text-xs font-semibold transition-colors ${
            mode === "bulk" ? "bg-primary-600 text-white" : "bg-primary-50 text-primary-600 hover:bg-primary-100"
          }`}
        >
          <Layers className="h-3.5 w-3.5" strokeWidth={2.25} />
          Bulk import
        </button>
      </div>

      {mode === "bulk" && (
        <div className="mt-4">
          <BulkImportPanel paperId={paper.id} token={token} onImported={async () => { await onBulkImported(); refreshMapped(); }} />
        </div>
      )}

      {mode === "single" && (
      <form onSubmit={handleSubmitQuestion} className="mt-4 flex flex-col gap-3">
        <div className="grid grid-cols-2 gap-3">
          <FormField label="Question No.">
            <TextInput required type="number" value={questionNumber} onChange={(e) => setQuestionNumber(e.target.value)} />
          </FormField>
          <FormField label="Subject">
            <TextInput required value={fields.subject} onChange={(e) => updateField("subject", e.target.value)} placeholder="e.g. Reasoning" />
          </FormField>
        </div>

        <div className="grid grid-cols-2 gap-3">
          <FormField label="Topic">
            <TextInput required value={fields.topic} onChange={(e) => updateField("topic", e.target.value)} placeholder="e.g. Blood Relations" />
          </FormField>
          <FormField label="Difficulty">
            <Select value={fields.difficultyLevel} onChange={(e) => updateField("difficultyLevel", e.target.value)}>
              {DIFFICULTIES.map((d) => <option key={d} value={d}>{d}</option>)}
            </Select>
          </FormField>
        </div>

        <FormField label="Question text">
          <TextArea required rows={3} value={fields.questionText} onChange={(e) => updateField("questionText", e.target.value)} />
        </FormField>
        <ImagePickerField label="Question image (optional)" file={images.questionImage} onChange={(f) => updateImage("questionImage", f)} />

        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          {["A", "B", "C", "D"].map((letter) => (
            <div key={letter} className="rounded-xl2 border border-primary-100 p-3">
              <FormField label={`Option ${letter}`}>
                <div className="flex items-center gap-2">
                  <input
                    type="radio"
                    name="correctOption"
                    checked={fields.correctOption === letter}
                    onChange={() => updateField("correctOption", letter)}
                    className="h-4 w-4 accent-mint-500"
                    title={`Mark ${letter} as the correct option`}
                  />
                  <TextInput
                    required
                    value={fields[`option${letter}`]}
                    onChange={(e) => updateField(`option${letter}`, e.target.value)}
                  />
                </div>
              </FormField>
              <ImagePickerField
                compact
                label={`Option ${letter} image`}
                file={images[`option${letter}Image`]}
                onChange={(f) => updateImage(`option${letter}Image`, f)}
              />
            </div>
          ))}
        </div>
        <p className="text-xs text-ink-400">Select the radio button next to the correct option.</p>

        <FormField label="Explanation (optional)">
          <TextArea rows={3} value={fields.explanation} onChange={(e) => updateField("explanation", e.target.value)} />
        </FormField>
        <ImagePickerField label="Explanation image (optional)" file={images.explanationImage} onChange={(f) => updateImage("explanationImage", f)} />

        <FormField label="Source reference (optional)">
          <TextInput value={fields.sourceReference} onChange={(e) => updateField("sourceReference", e.target.value)} />
        </FormField>

        {submitError && <Alert>{submitError}</Alert>}

        <div className="mt-2 flex flex-wrap gap-2">
          <Button type="submit" isLoading={submitting}>
            Upload &amp; Next
          </Button>
        </div>
      </form>
      )}

      <div className="mt-4 flex flex-wrap gap-2">
        <Button type="button" variant="secondary" isLoading={finishing} onClick={handleFinish} disabled={!mappedQuestions || mappedQuestions.length === 0}>
          Done — submit paper
        </Button>
      </div>
      {finishError && <Alert>{finishError}</Alert>}
        </>
      )}
    </div>
  );
}

function ImagePickerField({ label, file, onChange, compact }) {
  const inputRef = useRef(null);
  return (
    <div className={compact ? "mt-2" : ""}>
      {!compact && <span className="mb-1 block text-xs font-semibold text-ink-600">{label}</span>}
      <label className={`flex cursor-pointer items-center gap-2 rounded-xl2 border border-dashed border-primary-100 bg-white text-ink-600 hover:border-secondary-500 ${compact ? "px-2.5 py-1.5 text-xs" : "px-3.5 py-2.5 text-sm"}`}>
        <ImagePlus className={compact ? "h-3.5 w-3.5 text-ink-400" : "h-4 w-4 text-ink-400"} strokeWidth={2} />
        <span className="truncate">{file ? file.name : compact ? label : "Choose an image"}</span>
        <input
          ref={inputRef}
          type="file"
          accept=".png,.jpg,.jpeg,.webp,.svg"
          className="hidden"
          onChange={(e) => onChange(e.target.files?.[0] || null)}
        />
        {file && (
          <button
            type="button"
            onClick={(e) => {
              e.preventDefault();
              onChange(null);
              if (inputRef.current) inputRef.current.value = "";
            }}
            className="ml-auto text-ink-400 hover:text-red-500"
          >
            <X className="h-3.5 w-3.5" strokeWidth={2.5} />
          </button>
        )}
      </label>
    </div>
  );
}
