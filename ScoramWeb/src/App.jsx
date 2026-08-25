import { Routes, Route } from "react-router-dom";
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
import PreviousYearPapers from "./pages/PreviousYearPapers";
import Quizzes from "./pages/Quizzes";
import NotFound from "./pages/NotFound";
// LANDING PAGE -- public support pages linked from the new marketing footer
import PrivacyPolicy from "./pages/PrivacyPolicy";
import Terms from "./pages/Terms";
// SCORAM_TESTS
import Tests from "./pages/Tests";
import PracticeTests from "./pages/PracticeTests";
import MockTests from "./pages/MockTests";
import MyTests from "./pages/MyTests";
import TestAttemptResult from "./pages/TestAttemptResult";
import TestRunner from "./components/tests/TestRunner";
import PreExamInstructions from "./pages/PreExamInstructions";
// GAMIFICATION
import Leaderboard from "./pages/Leaderboard";
import ProgressPage from "./pages/Progress";
import Referrals from "./pages/Referrals";
import Bookmarks from "./pages/Bookmarks";
import Settings from "./pages/Settings";
import { AuthProvider } from "./context/AuthContext";
import { ChatConnectionProvider } from "./context/ChatConnectionContext";

function AppRoutes() {
  return (
    <Routes>
      <Route element={<AppLayout />}>
        <Route index element={<Home />} />
        <Route path="search" element={<SearchQuestions />} />
        {/* MASTER PROMPT -- Previous Year Paper Practice: "/pyq" used to just redirect into "Find
            PYQs" (individual-question browsing). It's now the real Previous Year Paper Practice
            destination -- browse Exam/Year/Language, then attempt the FULL paper as one timed test
            (see StudentPapersController.Start). "Find PYQs" still exists at /search, unchanged, for
            anyone who wants a single question rather than a full paper. */}
        <Route path="pyq" element={<PreviousYearPapers />} />
        <Route path="discussions" element={<Discussions />} />
        <Route path="questions/:questionId" element={<QuestionDetail />} />
        {/* SCORAM_QUESTION_BANK -- individual-question search engine, independent of the
            Paper/PYP upload flow above (spec section 21: two different features). */}
        <Route path="question-bank" element={<QuestionBankSearch />} />
        <Route path="question-bank/:questionId" element={<QuestionBankQuestionDetail />} />
        <Route path="chat" element={<GroupChat />} />

        {/* SCORAM_TESTS -- "Tests" is a hub (Practice Tests / Mock Tests). The old flat TestsList +
            one-shot attempt/result components (tests-classic, tests/:testId/attempt, tests/results/:id)
            were a parallel, pre-timer-fix implementation of this same flow and have been removed --
            keeping both around was what let students land on TestRunner.jsx via one path and the
            legacy TestAttempt.jsx via another, with no way to tell from the UI which one they'd get. */}
        <Route path="tests" element={<Tests />} />

        {/* Attempting/submitting/reviewing a test is inherently tied to a student's own
            account -- hard-redirect rather than letting a guest fill out a test only to
            fail at the very end. */}
        <Route element={<ProtectedRoute />}>
          <Route path="tests/practice" element={<PracticeTests />} />
          <Route path="tests/mock" element={<MockTests />} />
          <Route path="tests/my" element={<MyTests />} />
          <Route path="tests/instructions/:kind/:id" element={<PreExamInstructions />} />
          <Route path="tests/attempt/:attemptId" element={<TestRunner />} />
          <Route path="tests/result/:attemptId" element={<TestAttemptResult />} />
          <Route path="quizzes" element={<Quizzes />} />
          <Route path="profile" element={<Profile />} />
          <Route path="bookmarks" element={<Bookmarks />} />
          <Route path="settings" element={<Settings />} />

          {/* GAMIFICATION -- all per-student, so behind the same auth gate as the rest of this
              block (GamificationController/ReferralsController on the backend both require
              a logged-in student too). */}
          <Route path="leaderboard" element={<Leaderboard />} />
          <Route path="progress" element={<ProgressPage />} />
          <Route path="referrals" element={<Referrals />} />
        </Route>

        <Route path="*" element={<NotFound />} />
      </Route>

      <Route element={<AuthLayout />}>
        <Route path="login" element={<Login />} />
      </Route>

      {/* LANDING PAGE -- public support pages, own navbar/footer (LegalPage), no app chrome,
          so these are top-level routes rather than nested under AppLayout or AuthLayout. */}
      <Route path="privacy-policy" element={<PrivacyPolicy />} />
      <Route path="terms" element={<Terms />} />
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
