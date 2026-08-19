// ============================================================================
// MOCK DATA — for sections the backend doesn't have endpoints for yet.
// ============================================================================
// LIVE (wired to ScoramAPI, see src/api/):
//   - Auth (login/register)             -> src/context/AuthContext.jsx
//   - Questions search                  -> src/pages/SearchQuestions.jsx
//   - Discussions (global feed + reply) -> src/pages/Discussions.jsx,
//                                          src/components/home/TopDiscussions.jsx
//   - Mock Tests (list/attempt/result)  -> src/pages/Tests.jsx
//   - Recent Tests (attempt history)    -> src/components/home/RecentTests.jsx
//
// STILL MOCK (backend has no endpoint yet — see ScoramAPI/README.md "what's
// deliberately not in this slice"): Today's Challenge, Popular Exams counts,
// Gamification (streak/XP/badges/leaderboard). Kept here, shaped like their
// future DTOs, so swapping each one for a real fetch() later is a small change
// in one component rather than a rewrite.
// ============================================================================

// Streak/XP card on mobile — gamification isn't implemented on the backend yet
// (UserStreak/UserXP tables exist, but there's no controller for them), so this
// stays mock until that module is built.
export const streakPreview = {
  streakDays: 7,
  streakXP: 120,
};

export const sidebarNavItems = [
  { to: "/", label: "Home", icon: "Home" },
  { to: "/search", label: "PYQ Bank", icon: "BookOpen" },
  { to: "/question-bank", label: "Question Bank", icon: "Library" },
  { to: "/chat", label: "Group Chat", icon: "MessageCircle", highlight: "accent" },
  { to: "/tests", label: "Tests", icon: "ClipboardCheck", highlight: "mint" },
  { to: "/quizzes", label: "Quizzes", icon: "HelpCircle" },
  { to: "/discussions", label: "Discussions", icon: "MessageSquare" },
  { to: "/leaderboard", label: "Leaderboard", icon: "Trophy" },
  { to: "/bookmarks", label: "Bookmarks", icon: "Bookmark" },
  { to: "/progress", label: "My Progress", icon: "BarChart3" },
  { to: "/profile", label: "Profile", icon: "User" },
  { to: "/settings", label: "Settings", icon: "Settings" },
];

export const bottomNavItems = [
  { to: "/", label: "Home", icon: "Home" },
  { to: "/search", label: "Search", icon: "Search" },
  { key: "ask", label: "Ask", icon: "HelpCircle", isCta: true },
  { to: "/tests", label: "Tests", icon: "ClipboardCheck" },
  { to: "/profile", label: "Profile", icon: "User" },
];

export const heroStats = [
  { label: "PYQ Questions", value: "50K+", icon: "BookOpen" },
  { label: "Discussions", value: "10K+", icon: "MessageCircle" },
  { label: "Mock Tests", value: "2K+", icon: "ClipboardCheck" },
  { label: "Students", value: "100K+", icon: "Trophy" },
];

export const quickAccessItems = [
  {
    key: "pyq",
    label: "PYQ Questions",
    description: "Practice previous year questions",
    icon: "BookOpen",
    tint: "secondary",
  },
  {
    key: "mock",
    label: "Mock Tests",
    description: "Full length & chapter wise tests",
    icon: "ClipboardList",
    tint: "mint",
  },
  {
    key: "quizzes",
    label: "Quizzes",
    description: "Topic-wise quizzes & daily challenges",
    icon: "HelpCircle",
    tint: "violet",
  },
  {
    key: "test",
    label: "Test",
    description: "Timed exam-wise practice tests",
    icon: "ClipboardCheck",
    tint: "accent",
  },
  {
    key: "progress",
    label: "My Progress",
    description: "Track performance & improve daily",
    icon: "TrendingUp",
    tint: "teal",
  },
];

export const popularExams = [
  { id: "ssc-cgl", name: "SSC CGL", questionCount: 12845, badge: "🛡️", badgeBg: "bg-accent-50" },
  { id: "railway-ntpc", name: "Railway NTPC", questionCount: 9821, badge: "🚆", badgeBg: "bg-accent-50" },
  { id: "ssc-chsl", name: "SSC CHSL", questionCount: 7543, badge: "📗", badgeBg: "bg-mint-50" },
];

export const todaysChallenge = {
  examTag: "SSC CGL 2024",
  subjectTag: "Maths",
  difficultyTag: "Medium",
  questionText:
    "A man sold an article for ₹720 and incurred a loss of 10%. At what price should he sell it to gain 20%?",
  answerPreview: "Correct answer: ₹960",
  views: "12.5K",
  comments: 45,
  methods: 3,
};


