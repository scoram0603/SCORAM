import { getPaper, startPaper } from "../api/papers";
import { getMockTestSummary, startMockTest } from "../api/mockTests";
import { getPracticeTestTemplate, generatePracticeTest, startPracticeTestFromTemplate } from "../api/practiceTests";
import { previewWeakTopics, generateWeakTopicsQuiz, getDailyQuiz, startDailyQuiz } from "../api/quizzes";
import { getQuizChallenge, startQuizChallenge } from "../api/quizChallenges";

// Pre-Exam Instructions -- one reusable briefing screen in front of every existing "start an
// attempt" action (spec: "Universal Pre-Exam Instructions UI"). This module is the ONLY place that
// knows how to turn each of the 6 kinds into a common view model, and how to actually start each one
// once confirmed -- PreExamInstructions.jsx itself has no per-kind logic at all. Nothing about the
// attempt engine, timer, or scoring is touched here: every "start" call below is the exact same
// function each page already called directly before this screen existed (startPaper/startMockTest/
// generatePracticeTest/startPracticeTestFromTemplate/generateWeakTopicsQuiz/startDailyQuiz) --
// StartedAt/ExpiresAt are computed server-side the moment one of these actually runs, which is why
// this screen calls them only on confirm, never while just showing the briefing.
//
// "kind" values: "paper" | "mock" | "practice-template" | "practice-adhoc" | "quiz-daily" | "quiz-weak" | "quiz-challenge"
// practice-adhoc and quiz-weak have no id to look up by -- they're generated fresh from choices made
// on the page the student came from, passed here via router `state` (see PracticeTests.jsx/
// Quizzes.jsx). If that state is missing (direct link, page refresh), loadBriefing throws a
// recognizable error so the screen can send the student back to pick again instead of guessing.

function detailRows(pairs) {
  // Only non-empty values render -- spec: "Do not show empty fields."
  return pairs.filter(([, value]) => value !== null && value !== undefined && value !== "").map(([label, value]) => ({ label, value }));
}

export const MISSING_STATE_ERROR = "MISSING_STATE";

export async function loadBriefing(kind, id, state) {
  switch (kind) {
    case "paper": return loadPaper(id);
    case "mock": return loadMock(id);
    case "practice-template": return loadPracticeTemplate(id);
    case "practice-adhoc": return loadPracticeAdhoc(state);
    case "quiz-daily": return loadQuizDaily(id);
    case "quiz-weak": return loadQuizWeak(state);
    case "quiz-challenge": return loadQuizChallenge(id);
    default: throw new Error(`Unknown exam kind: ${kind}`);
  }
}

export async function confirmStart(kind, id, state) {
  switch (kind) {
    case "paper": return startPaper(id);
    case "mock": return startMockTest(id);
    case "practice-template": return startPracticeTestFromTemplate(id);
    case "practice-adhoc": return generatePracticeTest(state.filters);
    case "quiz-daily": return startDailyQuiz(id);
    case "quiz-weak": return generateWeakTopicsQuiz(state?.questionCount || 8);
    case "quiz-challenge": return startQuizChallenge(id);
    default: throw new Error(`Unknown exam kind: ${kind}`);
  }
}

// ---------- Previous Year Paper ----------
async function loadPaper(id) {
  const p = await getPaper(id);
  if (!p.isConfiguredForPractice || !p.isComplete) {
    return unavailableBriefing("paper", "/pyq", p.isConfiguredForPractice
      ? "Paper is currently unavailable because all questions have not been added yet."
      : "This paper isn't set up for timed practice yet.");
  }
  return {
    kind: "paper",
    badge: "PYP",
    typeLabel: "Previous Year Paper",
    title: `${p.examName} ${p.year}`,
    subtitle: [p.tier, p.shift].filter(Boolean).join(" · ") || null,
    details: detailRows([
      ["Exam", p.examName], ["Year", p.year], ["Tier", p.tier], ["Shift", p.shift],
      ["Language", p.language], ["Paper", p.paperLabel],
      ["Date", p.examDate ? new Date(p.examDate).toLocaleDateString("en-IN", { day: "numeric", month: "short", year: "numeric" }) : null],
    ]),
    questionCount: p.questionCount,
    durationMinutes: p.durationMinutes,
    negativeMarkingRatio: p.negativeMarkingRatio,
    instructions: null, // Papers don't carry admin free-text instructions -- generic list covers it
    startLabel: "Start Paper",
    backTo: "/pyq",
    whatToExpect: ["Real previous-year questions, in their original order", "Exam-like timed environment", "Question navigation & mark for review", "Automatic submission when time ends"],
  };
}

// ---------- Mock Test ----------
async function loadMock(id) {
  const t = await getMockTestSummary(id);
  if (t.status !== "Published" || t.availabilityStatus === "Completed") {
    return unavailableBriefing("mock", "/tests/mock", t.availabilityStatus === "Completed"
      ? "This Mock Test's window has closed."
      : "This Mock Test isn't available right now.");
  }
  if (t.availabilityStatus === "Upcoming") {
    return unavailableBriefing("mock", "/tests/mock", `This Mock Test hasn't started yet (opens ${new Date(t.scheduledAt).toLocaleString()}).`);
  }
  if (t.maxAttempts != null && t.myAttemptCount != null && t.myAttemptCount >= t.maxAttempts) {
    return unavailableBriefing("mock", "/tests/mock", `You've used all ${t.maxAttempts} attempt(s) for this Mock Test.`);
  }
  return {
    kind: "mock",
    badge: "MOCK",
    typeLabel: t.testType === "FullMockTest" ? "Full Mock Test" : "Mock Test",
    title: t.title,
    subtitle: t.examName || null,
    details: detailRows([
      ["Exam", t.examName],
      ["Closes", t.endAt ? new Date(t.endAt).toLocaleString() : null],
      ["Attempts used", t.maxAttempts != null ? `${t.myAttemptCount || 0} / ${t.maxAttempts}` : null],
    ]),
    questionCount: t.questionCount,
    durationMinutes: t.durationMinutes,
    negativeMarkingRatio: t.negativeMarkingRatio,
    instructions: t.instructions || null,
    startLabel: "Start Mock Test",
    backTo: "/tests/mock",
    whatToExpect: ["Full exam-length paper", "Exam-like timed environment", "Question navigation & mark for review", "Automatic submission when time ends"],
  };
}

// ---------- Practice Test (curated template) ----------
async function loadPracticeTemplate(id) {
  const t = await getPracticeTestTemplate(id);
  return {
    kind: "practice-template",
    badge: "PRACTICE",
    typeLabel: "Practice Test",
    title: t.title,
    subtitle: [t.subject, t.topic].filter(Boolean).join(" — ") || t.examName || null,
    details: detailRows([
      ["Subject", t.subject], ["Topic", t.topic], ["Exam", t.examName],
      ["Years", t.yearFrom && t.yearTo ? (t.yearFrom === t.yearTo ? `${t.yearFrom}` : `${t.yearFrom}–${t.yearTo}`) : null],
      ["Difficulty", t.difficulty],
    ]),
    questionCount: t.questionCount,
    durationMinutes: t.durationMinutes,
    negativeMarkingRatio: t.negativeMarkingRatio,
    instructions: t.description || null,
    startLabel: "Start Practice Test",
    backTo: "/tests/practice",
    whatToExpect: ["Focused practice on your chosen subject/topic", "Question navigation & mark for review", "Automatic submission when time ends"],
  };
}

// ---------- Practice Test (ad-hoc filters) ----------
function loadPracticeAdhoc(state) {
  if (!state?.filters) {
    const err = new Error(MISSING_STATE_ERROR);
    err.code = MISSING_STATE_ERROR;
    throw err;
  }
  const { filters, labels = {} } = state;
  return Promise.resolve({
    kind: "practice-adhoc",
    badge: "PRACTICE",
    typeLabel: "Practice Test",
    title: [labels.subjectName, labels.topicName].filter(Boolean).join(" — ") || "Custom Practice Test",
    subtitle: labels.examName || null,
    details: detailRows([
      ["Subject", labels.subjectName], ["Topic", labels.topicName], ["Exam", labels.examName],
      ["Difficulty", labels.difficultyLabel], ["Medium", labels.languageLabel],
    ]),
    questionCount: filters.questionCount,
    durationMinutes: filters.durationMinutes,
    negativeMarkingRatio: filters.negativeMarkingRatio,
    instructions: null,
    startLabel: "Start Practice Test",
    backTo: "/tests/practice",
    whatToExpect: ["Questions matching your chosen filters", "Question navigation & mark for review", "Automatic submission when time ends"],
  });
}

// ---------- Daily Quiz ----------
async function loadQuizDaily(id) {
  const q = await getDailyQuiz(id);
  if (q.availabilityStatus === "Upcoming") {
    return unavailableBriefing("quiz-daily", "/quizzes", "This quiz hasn't started yet.");
  }
  if (q.availabilityStatus !== "Live") {
    return unavailableBriefing("quiz-daily", "/quizzes", "This quiz isn't available right now.");
  }
  if (q.maxAttempts != null && q.myAttemptCount != null && q.myAttemptCount >= q.maxAttempts) {
    return unavailableBriefing("quiz-daily", "/quizzes", `You've used all ${q.maxAttempts} attempt(s) for this quiz.`);
  }
  return {
    kind: "quiz-daily",
    badge: "QUIZ",
    typeLabel: "Quiz",
    title: q.title,
    subtitle: q.topic || null,
    details: detailRows([["Topic", q.topic]]),
    questionCount: q.questionCount,
    durationMinutes: q.durationMinutes,
    negativeMarkingRatio: q.negativeMarkingRatio,
    instructions: null,
    startLabel: "Start Quiz",
    backTo: "/quizzes",
    whatToExpect: ["A quick, low-pressure round", "Question navigation", "Automatic submission when time ends"],
  };
}

// ---------- Weak Topics Quiz ----------
async function loadQuizWeak(state) {
  const questionCount = state?.questionCount || 8;
  const durationMinutes = Math.max(5, questionCount);
  let weakSubjects = [];
  try { weakSubjects = await previewWeakTopics(); } catch { /* fall back to no-subjects view below */ }

  return {
    kind: "quiz-weak",
    badge: "QUIZ",
    typeLabel: "Quiz",
    title: "Weak Topics Quiz",
    subtitle: weakSubjects.length > 0 ? weakSubjects.map((s) => s.subject).join(", ") : "General mixed quiz",
    details: detailRows([
      ["Based on", weakSubjects.length > 0 ? weakSubjects.map((s) => `${s.subject} (${Math.round(s.accuracy)}%)`).join(", ") : "No history yet -- general mix"],
    ]),
    questionCount,
    durationMinutes,
    negativeMarkingRatio: 0,
    instructions: null,
    startLabel: "Start Quiz",
    backTo: "/quizzes",
    whatToExpect: ["Auto-picked from your own answer history", "A quick, low-pressure round", "No negative marking"],
    state: { questionCount },
  };
}

function unavailableBriefing(kind, backTo, reason) {
  return { kind, unavailable: true, reason, backTo };
}

// ---------- Challenge a Friend (accepting) ----------
async function loadQuizChallenge(id) {
  const c = await getQuizChallenge(id);
  if (c.status === "Declined" || c.status === "Expired") {
    return unavailableBriefing("quiz-challenge", "/quizzes", c.status === "Declined"
      ? "This challenge was declined."
      : "This challenge has expired.");
  }
  const opponentName = c.iAmChallenger ? c.challengedName : c.challengerName;
  const opponentScore = c.iAmChallenger ? c.challengedScore : c.challengerScore;
  return {
    kind: "quiz-challenge",
    badge: "QUIZ",
    typeLabel: "Challenge",
    title: c.quizTitle,
    subtitle: `vs ${opponentName}`,
    details: detailRows([
      ["Opponent", opponentName],
      ["Their score", opponentScore != null ? opponentScore : "Not finished yet"],
    ]),
    questionCount: c.questionCount,
    durationMinutes: c.durationMinutes,
    negativeMarkingRatio: c.negativeMarkingRatio,
    instructions: null,
    startLabel: "Start Challenge",
    backTo: "/quizzes",
    whatToExpect: ["The exact same questions your opponent got", "A quick, low-pressure round", "See who wins once you finish"],
  };
}
