import { Routes, Route, Navigate } from "react-router-dom";
import AppLayout from "./layouts/AppLayout";
import AuthLayout from "./layouts/AuthLayout";
import ProtectedRoute from "./routes/ProtectedRoute";
import Home from "./pages/Home";
import Login from "./pages/Login";
import Profile from "./pages/Profile";
import SearchQuestions from "./pages/SearchQuestions";
import Discussions from "./pages/Discussions";
import GroupChat from "./pages/GroupChat";
import QuestionDetail from "./pages/QuestionDetail";
import QuestionBankSearch from "./pages/QuestionBankSearch";
import QuestionBankQuestionDetail from "./pages/QuestionBankQuestionDetail";
import TestResultPage from "./pages/TestResultPage";
import NotFound from "./pages/NotFound";
import TestsList from "./components/tests/TestsList";
import TestAttempt from "./components/tests/TestAttempt";
// SCORAM_TESTS
import Tests from "./pages/Tests";
import PracticeTests from "./pages/PracticeTests";
import MockTests from "./pages/MockTests";
import MyTests from "./pages/MyTests";
import TestAttemptResult from "./pages/TestAttemptResult";
import TestRunner from "./components/tests/TestRunner";
import { AuthProvider } from "./context/AuthContext";
import { ChatConnectionProvider } from "./context/ChatConnectionContext";

// Cards for these already exist on Home (Quizzes, Leaderboard, Bookmarks, My Progress,
// Settings) but the features themselves aren't built yet -- routed honestly to a "next up
// on the build list" screen instead of a dead link or a 404.
const COMING_SOON_LABEL = {
  quizzes: "Quizzes",
  leaderboard: "Leaderboard",
  bookmarks: "Bookmarks",
  progress: "My Progress",
  settings: "Settings",
};

function ComingSoon({ label }) {
  return (
    <div className="flex h-full min-h-[70vh] flex-col items-center justify-center gap-2 px-8 text-center">
      <p className="text-lg font-bold text-ink-900">{label}</p>
      <p className="text-sm text-ink-400">This screen is next up on the build list.</p>
    </div>
  );
}

function AppRoutes() {
  return (
    <Routes>
      <Route element={<AppLayout />}>
        <Route index element={<Home />} />
        <Route path="search" element={<SearchQuestions />} />
        {/* Browse-by-Exam inside Find PYQs now covers exactly what a separate
            "PYQ Bank" screen used to promise, so it's a redirect rather than a second page. */}
        <Route path="pyq" element={<Navigate to="/search" replace />} />
        <Route path="discussions" element={<Discussions />} />
        <Route path="questions/:questionId" element={<QuestionDetail />} />
        {/* SCORAM_QUESTION_BANK -- individual-question search engine, independent of the
            Paper/PYP upload flow above (spec section 21: two different features). */}
        <Route path="question-bank" element={<QuestionBankSearch />} />
        <Route path="question-bank/:questionId" element={<QuestionBankQuestionDetail />} />
        <Route path="chat" element={<GroupChat />} />

        {/* SCORAM_TESTS -- "Tests" is now a hub (Practice Tests / Mock Tests), replacing the old
            flat TestsList as the default landing. The old list + one-shot attempt/result components
            are kept mounted at their original paths below for backward compatibility, just no longer
            the primary way in. */}
        <Route path="tests" element={<Tests />} />
        <Route path="tests-classic" element={<TestsList />} />

        {/* Attempting/submitting/reviewing a test is inherently tied to a student's own
            account (submitAttempt and getAttemptDetail both require auth) -- hard-redirect
            rather than letting a guest fill out a test only to fail at the very end. */}
        <Route element={<ProtectedRoute />}>
          <Route path="tests/:testId/attempt" element={<TestAttempt />} />
          <Route path="tests/results/:attemptId" element={<TestResultPage />} />
          <Route path="tests/practice" element={<PracticeTests />} />
          <Route path="tests/mock" element={<MockTests />} />
          <Route path="tests/my" element={<MyTests />} />
          <Route path="tests/attempt/:attemptId" element={<TestRunner />} />
          <Route path="tests/result/:attemptId" element={<TestAttemptResult />} />
          <Route path="profile" element={<Profile />} />
        </Route>

        {Object.entries(COMING_SOON_LABEL).map(([key, label]) => (
          <Route key={key} path={key} element={<ComingSoon label={label} />} />
        ))}

        <Route path="*" element={<NotFound />} />
      </Route>

      <Route element={<AuthLayout />}>
        <Route path="login" element={<Login />} />
      </Route>
    </Routes>
  );
}

export default function App() {
  return (
    <AuthProvider>
      <ChatConnectionProvider>
        <AppRoutes />
      </ChatConnectionProvider>
    </AuthProvider>
  );
}
