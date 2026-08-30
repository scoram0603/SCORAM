import { useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import {
  ArrowLeft, Loader2, CheckCircle2, XCircle, MinusCircle, Clock, Target, ChevronDown, Flag,
  Swords, Search, Check, X as XIcon,
} from "lucide-react";
import { getAttempt } from "../api/testAttempts";
import { API_BASE_URL } from "../api/client";
import { createQuizChallenge, getChallengesByAttempt } from "../api/quizChallenges";
import { searchUsers } from "../api/directMessages";
import { listChatRooms } from "../api/chat";
import SolutionsPanel from "../components/questions/SolutionsPanel";
import CommentThread from "../components/questions/CommentThread";
import LikeButton from "../components/questions/LikeButton";
import ReportQuestionModal from "../components/questions/ReportQuestionModal";
import { MathText, RichQuestionBody } from "../components/questions/MathText";

function imgSrc(url) {
  if (!url) return null;
  return url.startsWith("http") ? url : `${API_BASE_URL}${url}`;
}

const OPTION_LETTERS = ["A", "B", "C", "D"];

export default function TestAttemptResult() {
  const { attemptId } = useParams();
  const navigate = useNavigate();
  const [result, setResult] = useState(null);
  const [status, setStatus] = useState("loading");
  const [expandedIndex, setExpandedIndex] = useState(0);

  useEffect(() => {
    let cancelled = false;
    getAttempt(attemptId)
      .then((data) => {
        if (cancelled) return;
        setResult(data);
        setStatus("success");
      })
      .catch(() => {
        if (!cancelled) setStatus("error");
      });
    return () => {
      cancelled = true;
    };
  }, [attemptId]);

  if (status === "loading") {
    return (
      <div className="flex min-h-[60vh] items-center justify-center text-ink-400">
        <Loader2 className="h-6 w-6 animate-spin" strokeWidth={2.25} />
      </div>
    );
  }

  if (status === "error" || !result) {
    return (
      <div className="px-4 py-10 text-center">
        <p className="text-sm text-ink-600">Couldn't load this result.</p>
        <button type="button" onClick={() => navigate("/tests/my")} className="mt-2 text-sm font-semibold text-secondary-500">
          Back to My Tests
        </button>
      </div>
    );
  }

  return (
    <div className="px-4 pb-10 pt-4 sm:px-6 lg:px-8 lg:pt-6">
      <button type="button" onClick={() => navigate("/tests/my")} className="flex items-center gap-1.5 text-sm font-semibold text-secondary-500">
        <ArrowLeft className="h-4 w-4" strokeWidth={2.5} />
        My Tests
      </button>

      <div className="mt-3 rounded-xl2 border border-primary-100 bg-white p-5 shadow-card">
        <p className="text-xs font-bold uppercase tracking-wide text-secondary-500">
          {result.testKind === "Mock" ? "Mock Test"
            : result.testKind === "PreviousYearPaper" ? "Previous Year Paper"
            : result.testKind === "Quiz" ? "Quiz"
            : "Practice Test"}
        </p>
        <h1 className="mt-1 text-lg font-extrabold text-ink-900">{result.title}</h1>

        <div className="mt-4 flex items-end gap-2">
          <span className="text-3xl font-extrabold text-ink-900">{result.score}</span>
          <span className="pb-1 text-sm text-ink-400">/ {result.maxPossibleScore}</span>
          <span className="ml-auto rounded-xl2 bg-primary-50 px-3 py-1.5 text-sm font-bold text-primary-600">{result.percentageScore}%</span>
        </div>

        <div className="mt-4 grid grid-cols-3 gap-2 sm:grid-cols-5">
          <Stat icon={CheckCircle2} color="text-mint-500" label="Correct" value={result.correctCount} />
          <Stat icon={XCircle} color="text-red-500" label="Wrong" value={result.wrongCount} />
          <Stat icon={MinusCircle} color="text-ink-400" label="Skipped" value={result.skippedCount} />
          <Stat icon={Target} color="text-secondary-500" label="Accuracy" value={`${result.accuracyPercent}%`} />
          <Stat icon={Clock} color="text-accent-600" label="Time" value={formatDuration(result.timeTakenSeconds)} />
        </div>
        {result.rank && <p className="mt-3 text-xs font-semibold text-ink-400">Rank #{result.rank}{result.percentile ? ` · ${result.percentile}th percentile` : ""}</p>}
      </div>

      {result.testKind === "Quiz" && <ChallengeComparisonCards attemptId={attemptId} />}
      {result.testKind === "Quiz" && <ChallengeFriendCard attemptId={attemptId} />}

      <h2 className="mt-6 text-sm font-bold text-ink-900">Question-wise Analysis</h2>
      <div className="mt-2 flex flex-col gap-2">
        {result.questions.map((q, i) => (
          <QuestionReviewCard
            key={q.studentAnswerId}
            question={q}
            index={i}
            expanded={expandedIndex === i}
            onToggle={() => setExpandedIndex(expandedIndex === i ? -1 : i)}
          />
        ))}
      </div>
    </div>
  );
}

function Stat({ icon: Icon, color, label, value }) {
  return (
    <div className="flex flex-col items-center rounded-lg bg-surface p-2.5 text-center">
      <Icon className={`h-4 w-4 ${color}`} strokeWidth={2.25} />
      <span className="mt-1 text-sm font-extrabold text-ink-900">{value}</span>
      <span className="text-[10px] font-medium text-ink-400">{label}</span>
    </div>
  );
}

function QuestionReviewCard({ question: q, index, expanded, onToggle }) {
  const [reportOpen, setReportOpen] = useState(false);
  const navigate = useNavigate();
  const questionType = q.isQuestionBank ? "bank" : "paper";
  const sourceQuestionId = q.isQuestionBank ? q.sourceQuestionBankQuestionId : q.sourceQuestionId;

  return (
    <div className={`rounded-xl2 border bg-white shadow-card ${q.wasSkipped ? "border-primary-100" : q.isCorrect ? "border-mint-200" : "border-red-200"}`}>
      <button type="button" onClick={onToggle} className="flex w-full items-center gap-3 p-4 text-left">
        <span className={`flex h-7 w-7 shrink-0 items-center justify-center rounded-full text-xs font-bold ${
          q.wasSkipped ? "bg-primary-50 text-ink-400" : q.isCorrect ? "bg-mint-50 text-mint-500" : "bg-red-50 text-red-500"
        }`}>
          {index + 1}
        </span>
        <span className="min-w-0 flex-1 truncate text-sm font-medium text-ink-900">{q.questionText}</span>
        {q.wasSkipped ? (
          <MinusCircle className="h-4 w-4 shrink-0 text-ink-400" strokeWidth={2} />
        ) : q.isCorrect ? (
          <CheckCircle2 className="h-4 w-4 shrink-0 text-mint-500" strokeWidth={2.25} />
        ) : (
          <XCircle className="h-4 w-4 shrink-0 text-red-500" strokeWidth={2.25} />
        )}
        <ChevronDown className={`h-4 w-4 shrink-0 text-ink-400 transition-transform ${expanded ? "rotate-180" : ""}`} strokeWidth={2} />
      </button>

      {expanded && (
        <div className="border-t border-primary-100 px-4 pb-4 pt-3">
          <p className="mb-3 text-sm font-medium text-ink-900">
            <RichQuestionBody contentBlocks={q.contentBlocks} fallbackText={q.questionText} />
          </p>
          {imgSrc(q.questionImageUrl) && (
            <img src={imgSrc(q.questionImageUrl)} alt="" className="mb-3 max-h-56 rounded-lg border border-primary-100" />
          )}

          <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
            {OPTION_LETTERS.map((letter) => {
              const isCorrectOption = q.correctOption === letter;
              const isSelected = q.selectedOption === letter;
              return (
                <div
                  key={letter}
                  className={`flex items-start gap-1.5 rounded-lg border px-3 py-2 text-sm ${
                    isCorrectOption
                      ? "border-mint-500 bg-mint-50 text-mint-700"
                      : isSelected
                      ? "border-red-400 bg-red-50 text-red-600"
                      : "border-primary-100 text-ink-600"
                  }`}
                >
                  <span className="font-bold">{letter}.</span>
                  <span className="min-w-0 flex-1">
                    <MathText text={q[`option${letter}`]} />
                    {imgSrc(q[`option${letter}ImageUrl`]) && (
                      <img src={imgSrc(q[`option${letter}ImageUrl`])} alt="" className="mt-1.5 max-h-28 rounded border border-primary-100" />
                    )}
                  </span>
                  {isCorrectOption && <CheckCircle2 className="h-4 w-4 shrink-0 text-mint-500" strokeWidth={2.25} />}
                  {isSelected && !isCorrectOption && <XCircle className="h-4 w-4 shrink-0 text-red-500" strokeWidth={2.25} />}
                </div>
              );
            })}
          </div>

          {q.explanation && (
            <div className="mt-3 rounded-lg bg-primary-50/60 p-3 text-sm leading-snug text-ink-600">
              <p className="mb-1 text-xs font-bold text-primary-600">Explanation</p>
              <MathText text={q.explanation} />
              {imgSrc(q.explanationImageUrl) && (
                <img src={imgSrc(q.explanationImageUrl)} alt="" className="mt-2 max-h-56 rounded-lg border border-primary-100" />
              )}
            </div>
          )}

          {(q.subject || q.topic) && (
            <p className="mt-2 text-xs text-ink-400">{[q.subject, q.topic].filter(Boolean).join(" / ")}</p>
          )}

          {sourceQuestionId && (
            <>
              <div className="mt-3 flex items-center justify-between border-t border-primary-100 pt-3">
                <LikeButton
                  questionId={sourceQuestionId}
                  likeCount={0}
                  dislikeCount={0}
                  myVote={null}
                  questionType={questionType}
                  onRequireLogin={() => navigate(`/login?redirect=/tests/my`)}
                />
                <button
                  type="button"
                  onClick={() => setReportOpen(true)}
                  className="flex items-center gap-1.5 text-xs font-semibold text-ink-400 hover:text-red-500"
                >
                  <Flag className="h-3.5 w-3.5" strokeWidth={2} />
                  Report Question
                </button>
              </div>

              <SolutionsPanel questionId={sourceQuestionId} questionType={questionType} onRequireLogin={() => navigate(`/login?redirect=/tests/my`)} />
              <CommentThread questionId={sourceQuestionId} questionType={questionType} onRequireLogin={() => navigate(`/login?redirect=/tests/my`)} />

              <ReportQuestionModal questionId={sourceQuestionId} questionType={questionType} open={reportOpen} onClose={() => setReportOpen(false)} />
            </>
          )}
        </div>
      )}
    </div>
  );
}

function formatDuration(totalSeconds) {
  const m = Math.floor(totalSeconds / 60);
  const s = totalSeconds % 60;
  return m > 0 ? `${m}m ${s}s` : `${s}s`;
}

// Phase 3, "Challenge a Friend" -- freezes THIS exact quiz (same questions, same order) for a
// friend to attempt too, so the score comparison is fair. See QuizChallengesController.
// Fixes the gap where a challenge could be sent/accepted but neither side ever saw who won.
// Fetches EVERY challenge this specific attempt is part of -- as the SourceAttempt (could be several,
// if the challenger sent this quiz to multiple people/a group) or as a ChallengedAttempt (exactly
// one). Renders nothing if this attempt isn't part of any challenge.
function ChallengeComparisonCards({ attemptId }) {
  const [challenges, setChallenges] = useState(null);

  useEffect(() => {
    getChallengesByAttempt(attemptId).then(setChallenges).catch(() => setChallenges([]));
  }, [attemptId]);

  if (!challenges || challenges.length === 0) return null;

  return (
    <div className="mt-4 flex flex-col gap-2">
      {challenges.map((c) => (
        <ChallengeComparisonCard key={c.id} challenge={c} />
      ))}
    </div>
  );
}

function ChallengeComparisonCard({ challenge: c }) {
  const me = c.iAmChallenger
    ? { name: "You", score: c.challengerScore }
    : { name: "You", score: c.challengedScore };
  const opponent = c.iAmChallenger
    ? { name: c.challengedName, score: c.challengedScore }
    : { name: c.challengerName, score: c.challengerScore };

  const iWon = c.status === "Completed" && (
    (c.iAmChallenger && c.winner === "Challenger") || (!c.iAmChallenger && c.winner === "Challenged")
  );
  const isTie = c.status === "Completed" && c.winner === "Tie";

  return (
    <div className={`rounded-xl2 border p-4 ${
      c.status !== "Completed" ? "border-primary-100 bg-white"
        : iWon ? "border-mint-200 bg-mint-50"
        : isTie ? "border-primary-200 bg-primary-50"
        : "border-red-200 bg-red-50"
    }`}>
      <p className="flex items-center gap-1.5 text-xs font-bold text-ink-900">
        <Swords className="h-3.5 w-3.5 text-secondary-500" strokeWidth={2.25} />
        {c.iAmChallenger ? `You challenged ${c.challengedName}` : `${c.challengerName} challenged you`}
      </p>

      {c.status === "Pending" && (
        <p className="mt-1.5 text-xs text-ink-400">Waiting for {c.iAmChallenger ? c.challengedName : "you"} to attempt it.</p>
      )}
      {c.status === "InProgress" && (
        <p className="mt-1.5 text-xs text-ink-400">{c.iAmChallenger ? `${c.challengedName} has started -- not finished yet.` : "You've started -- finish to see who won."}</p>
      )}
      {c.status === "Declined" && <p className="mt-1.5 text-xs text-ink-400">Declined.</p>}
      {c.status === "Expired" && <p className="mt-1.5 text-xs text-ink-400">This challenge expired before it was accepted.</p>}

      {c.status === "Completed" && (
        <>
          <div className="mt-2 flex items-center justify-between gap-3">
            <ScoreChip label={me.name} score={me.score} highlight={iWon} />
            <span className="text-xs font-bold text-ink-300">vs</span>
            <ScoreChip label={opponent.name} score={opponent.score} highlight={!iWon && !isTie} />
          </div>
          <p className={`mt-2 text-center text-xs font-bold ${iWon ? "text-mint-600" : isTie ? "text-ink-500" : "text-red-500"}`}>
            {isTie ? "It's a tie!" : iWon ? "You won! 🎉" : `${opponent.name} won this one.`}
          </p>
        </>
      )}
    </div>
  );
}

function ScoreChip({ label, score, highlight }) {
  return (
    <span className={`flex-1 rounded-lg py-2 text-center ${highlight ? "bg-mint-100" : "bg-white"}`}>
      <span className="block text-[10px] font-semibold text-ink-400">{label}</span>
      <span className="block text-lg font-extrabold text-ink-900">{score}</span>
    </span>
  );
}

function ChallengeFriendCard({ attemptId }) {
  const [open, setOpen] = useState(false);
  const [tab, setTab] = useState("friends"); // "friends" | "group"

  const [search, setSearch] = useState("");
  const [results, setResults] = useState([]);
  const [searching, setSearching] = useState(false);
  const [selectedFriends, setSelectedFriends] = useState([]); // [{ id, fullName, username }]

  const [rooms, setRooms] = useState(null);
  const [roomsStatus, setRoomsStatus] = useState("idle");
  const [selectedGroup, setSelectedGroup] = useState(null); // { id, examName }

  const [sending, setSending] = useState(false);
  const [sentResult, setSentResult] = useState(null); // QuizChallengeBatchResultDto
  const [error, setError] = useState("");

  useEffect(() => {
    if (tab !== "friends" || search.trim().length < 2) {
      setResults([]);
      return;
    }
    setSearching(true);
    const handle = setTimeout(() => {
      searchUsers(search)
        .then(setResults)
        .catch(() => setResults([]))
        .finally(() => setSearching(false));
    }, 300);
    return () => clearTimeout(handle);
  }, [search, tab]);

  useEffect(() => {
    if (tab !== "group" || roomsStatus !== "idle") return;
    setRoomsStatus("loading");
    listChatRooms()
      .then((res) => {
        setRooms(res.filter((r) => r.isMember));
        setRoomsStatus("success");
      })
      .catch(() => setRoomsStatus("error"));
  }, [tab, roomsStatus]);

  function toggleFriend(u) {
    setSelectedFriends((prev) =>
      prev.some((f) => f.id === u.id) ? prev.filter((f) => f.id !== u.id) : [...prev, u]
    );
  }

  const canSend = selectedFriends.length > 0 || Boolean(selectedGroup);

  async function handleSend() {
    setError("");
    setSending(true);
    try {
      const result = await createQuizChallenge(attemptId, {
        challengedUserIds: selectedFriends.map((f) => f.id),
        challengedGroupId: selectedGroup?.id,
      });
      setSentResult(result);
    } catch (err) {
      setError(err.message || "Couldn't send that challenge.");
    } finally {
      setSending(false);
    }
  }

  if (sentResult) {
    const count = sentResult.challenges.length;
    return (
      <div className="mt-4 flex items-start gap-2 rounded-xl2 border border-mint-200 bg-mint-50 p-4 text-sm font-semibold text-mint-700">
        <Check className="mt-0.5 h-4 w-4 shrink-0" strokeWidth={2.5} />
        <span>
          Challenge sent to {count} {count === 1 ? "person" : "people"}
          {sentResult.skippedCount > 0 ? ` (${sentResult.skippedCount} already challenged, skipped)` : ""} —
          you'll see how they did once they finish.
        </span>
      </div>
    );
  }

  return (
    <div className="mt-4 rounded-xl2 border border-primary-100 bg-white p-4 shadow-card">
      {!open ? (
        <button
          type="button"
          onClick={() => setOpen(true)}
          className="flex w-full items-center justify-center gap-2 rounded-xl2 border border-secondary-200 bg-secondary-50 py-2.5 text-sm font-semibold text-secondary-600 hover:bg-secondary-100"
        >
          <Swords className="h-4 w-4" strokeWidth={2.25} />
          Challenge Friends or a Group to this Quiz
        </button>
      ) : (
        <>
          <div className="flex gap-1.5">
            <button
              type="button"
              onClick={() => setTab("friends")}
              className={`h-8 flex-1 rounded-xl text-xs font-semibold ${tab === "friends" ? "bg-primary-600 text-white" : "bg-primary-50 text-ink-500"}`}
            >
              Friends {selectedFriends.length > 0 ? `(${selectedFriends.length})` : ""}
            </button>
            <button
              type="button"
              onClick={() => setTab("group")}
              className={`h-8 flex-1 rounded-xl text-xs font-semibold ${tab === "group" ? "bg-primary-600 text-white" : "bg-primary-50 text-ink-500"}`}
            >
              A Group {selectedGroup ? "(1)" : ""}
            </button>
          </div>

          {tab === "friends" && (
            <>
              {selectedFriends.length > 0 && (
                <div className="mt-3 flex flex-wrap gap-1.5">
                  {selectedFriends.map((f) => (
                    <span key={f.id} className="flex items-center gap-1 rounded-full bg-secondary-50 py-1 pl-2.5 pr-1.5 text-xs font-semibold text-secondary-600">
                      {f.fullName}
                      <button type="button" onClick={() => toggleFriend(f)} className="rounded-full p-0.5 hover:bg-secondary-100">
                        <XIcon className="h-3 w-3" strokeWidth={2.5} />
                      </button>
                    </span>
                  ))}
                </div>
              )}
              <label className="relative mt-2 block">
                <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-ink-400" strokeWidth={2} />
                <input
                  type="text"
                  autoFocus
                  value={search}
                  onChange={(e) => setSearch(e.target.value)}
                  placeholder="Search by username or name..."
                  className="h-10 w-full rounded-xl2 border border-primary-100 bg-white pl-9 pr-3 text-sm focus:border-secondary-500"
                />
              </label>

              <div className="mt-2 max-h-52 overflow-y-auto">
                {searching && (
                  <div className="flex justify-center py-3 text-ink-400"><Loader2 className="h-4 w-4 animate-spin" strokeWidth={2.25} /></div>
                )}
                {!searching && search.trim().length >= 2 && results.length === 0 && (
                  <p className="py-3 text-center text-xs text-ink-400">No students found.</p>
                )}
                {results.map((u) => {
                  const isSelected = selectedFriends.some((f) => f.id === u.id);
                  return (
                    <button
                      key={u.id}
                      type="button"
                      onClick={() => toggleFriend(u)}
                      className={`flex w-full items-center justify-between gap-2 rounded-lg px-2 py-2 text-left text-sm ${isSelected ? "bg-secondary-50" : "hover:bg-primary-50"}`}
                    >
                      <span>
                        <span className="font-semibold text-ink-900">{u.fullName}</span>
                        <span className="ml-1.5 text-xs text-ink-400">@{u.username}</span>
                      </span>
                      {isSelected ? <Check className="h-3.5 w-3.5 text-secondary-500" strokeWidth={2.5} /> : <Swords className="h-3.5 w-3.5 text-ink-300" strokeWidth={2.25} />}
                    </button>
                  );
                })}
              </div>
            </>
          )}

          {tab === "group" && (
            <div className="mt-2 max-h-60 overflow-y-auto">
              {roomsStatus === "loading" && (
                <div className="flex justify-center py-3 text-ink-400"><Loader2 className="h-4 w-4 animate-spin" strokeWidth={2.25} /></div>
              )}
              {roomsStatus === "success" && rooms.length === 0 && (
                <p className="py-3 text-center text-xs text-ink-400">Join a group in Group Chat first to challenge it.</p>
              )}
              {rooms?.map((r) => {
                const isSelected = selectedGroup?.id === r.id;
                return (
                  <button
                    key={r.id}
                    type="button"
                    onClick={() => setSelectedGroup(isSelected ? null : { id: r.id, examName: r.examName })}
                    className={`flex w-full items-center justify-between gap-2 rounded-lg px-2 py-2 text-left text-sm ${isSelected ? "bg-secondary-50" : "hover:bg-primary-50"}`}
                  >
                    <span>
                      <span className="font-semibold text-ink-900">{r.examName}</span>
                      <span className="ml-1.5 text-xs text-ink-400">{r.memberCount} members</span>
                    </span>
                    {isSelected ? <Check className="h-3.5 w-3.5 text-secondary-500" strokeWidth={2.5} /> : <Swords className="h-3.5 w-3.5 text-ink-300" strokeWidth={2.25} />}
                  </button>
                );
              })}
            </div>
          )}

          {error && <p className="mt-2 text-xs font-medium text-red-600">{error}</p>}

          <button
            type="button"
            onClick={handleSend}
            disabled={!canSend || sending}
            className="mt-3 flex h-10 w-full items-center justify-center gap-2 rounded-xl2 bg-primary-600 text-sm font-semibold text-white hover:bg-primary-700 disabled:opacity-50"
          >
            {sending ? <Loader2 className="h-4 w-4 animate-spin" strokeWidth={2.25} /> : <Swords className="h-4 w-4" strokeWidth={2.25} />}
            Send Challenge
          </button>
        </>
      )}
    </div>
  );
}
