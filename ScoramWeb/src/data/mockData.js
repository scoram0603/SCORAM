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

// Streak/XP card on mobile is now live (GET /api/gamification/me) -- see StreakXPCard.jsx.
//
// MASTER PROMPT -- Admin + Student Navigation Redesign: grouped into sections (Main/Practice/
// Community/My Learning/Account) instead of one long flat list, per the target information
// architecture. Existing routes are untouched -- only how they're labelled/grouped changed (see
// PYP Practice below, still "/pyq" -- previously that route redirected to "/search"; now it hosts
// the real Previous Year Paper Practice page, see App.jsx and pages/PreviousYearPapers.jsx).
// Sidebar.jsx/MobileDrawer.jsx both render this as: for each group, an (optional) small uppercase
// label followed by its items -- a group with a single item and section: null skips the label
// entirely, so "Home" alone doesn't get its own redundant "MAIN" heading.
export const sidebarNavItems = [
  {
    section: null,
    items: [{ to: "/", label: "Home", icon: "Home" }],
  },
  {
    section: "Practice",
    items: [
      { to: "/pyq", label: "PYP Practice", icon: "BookOpen" },
      { to: "/question-bank", label: "Question Bank", icon: "Library" },
      { to: "/tests", label: "Tests", icon: "ClipboardCheck", highlight: "mint" },
      { to: "/quizzes", label: "Quizzes", icon: "HelpCircle" },
    ],
  },
  {
    section: "Community",
    items: [
      { to: "/chat", label: "Group Chat", icon: "MessageCircle", highlight: "accent" },
      { to: "/discussions", label: "Discussions", icon: "MessageSquare" },
      { to: "/leaderboard", label: "Leaderboard", icon: "Trophy" },
    ],
  },
  {
    section: "My Learning",
    items: [
      { to: "/bookmarks", label: "Bookmarks", icon: "Bookmark" },
      { to: "/progress", label: "My Progress", icon: "BarChart3" },
    ],
  },
  {
    section: "Account",
    items: [
      { to: "/profile", label: "Profile", icon: "User" },
      { to: "/settings", label: "Settings", icon: "Settings" },
    ],
  },
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


