import { Routes, Route, Navigate } from "react-router-dom";
import AppLayout from "./layouts/AppLayout";
import AuthLayout from "./layouts/AuthLayout";
import ProtectedRoute from "./routes/ProtectedRoute";
import Login from "./pages/Login";
import NotFound from "./pages/NotFound";
import { lazyWithRetry } from "./components/routing/lazyWithRetry";
import { SuspenseOutlet } from "./components/routing/RouteSuspense";
import { AuthProvider } from "./context/AuthContext";
import { MyExamsProvider } from "./context/MyExamsContext";
import { ChatConnectionProvider } from "./context/ChatConnectionContext";

// ROUTE-LEVEL CODE SPLITTING -- only the app shell, Login and NotFound ship in the entry bundle;
// every other page downloads on first visit. lazyWithRetry also recovers from a deployment replacing
// chunk files while a tab is open. Suspense + error boundaries live in the layouts (around their
// <Outlet />) so the header/sidebar stay put while the page area loads.
const Home = lazyWithRetry(() => import("./pages/Home"));
const ForgotPassword = lazyWithRetry(() => import("./pages/ForgotPassword"));
const Profile = lazyWithRetry(() => import("./pages/Profile"));
const SearchQuestions = lazyWithRetry(() => import("./pages/SearchQuestions"));
const Discussions = lazyWithRetry(() => import("./pages/Discussions"));
const GroupChat = lazyWithRetry(() => import("./pages/GroupChat"));
const QuestionDetail = lazyWithRetry(() => import("./pages/QuestionDetail"));
const QuestionBankSearch = lazyWithRetry(() => import("./pages/QuestionBankSearch"));
const QuestionBankQuestionDetail = lazyWithRetry(() => import("./pages/QuestionBankQuestionDetail"));
const PreviousYearPapers = lazyWithRetry(() => import("./pages/PreviousYearPapers"));
const Quizzes = lazyWithRetry(() => import("./pages/Quizzes"));
const OurTeam = lazyWithRetry(() => import("./pages/OurTeam"));
const PrivacyPolicy = lazyWithRetry(() => import("./pages/PrivacyPolicy"));
const Terms = lazyWithRetry(() => import("./pages/Terms"));
const DeleteAccount = lazyWithRetry(() => import("./pages/DeleteAccount"));
const Tests = lazyWithRetry(() => import("./pages/Tests"));
const PracticeTests = lazyWithRetry(() => import("./pages/PracticeTests"));
const MockTests = lazyWithRetry(() => import("./pages/MockTests"));
const MyTests = lazyWithRetry(() => import("./pages/MyTests"));
const TestAttemptResult = lazyWithRetry(() => import("./pages/TestAttemptResult"));
const TestRunner = lazyWithRetry(() => import("./components/tests/TestRunner"));
const PreExamInstructions = lazyWithRetry(() => import("./pages/PreExamInstructions"));
const Leaderboard = lazyWithRetry(() => import("./pages/Leaderboard"));
const ProgressPage = lazyWithRetry(() => import("./pages/Progress"));
const Referrals = lazyWithRetry(() => import("./pages/Referrals"));
const TopicProgress = lazyWithRetry(() => import("./pages/TopicProgress"));
const MistakeBook = lazyWithRetry(() => import("./pages/MistakeBook"));
const Revision = lazyWithRetry(() => import("./pages/Revision"));
const StudyPlan = lazyWithRetry(() => import("./pages/StudyPlan"));
const StudyPartners = lazyWithRetry(() => import("./pages/StudyPartners"));
const Bookmarks = lazyWithRetry(() => import("./pages/Bookmarks"));
const Settings = lazyWithRetry(() => import("./pages/Settings"));
const RateScoram = lazyWithRetry(() => import("./pages/RateScoram"));
const SelectExams = lazyWithRetry(() => import("./pages/SelectExams"));
const MyExams = lazyWithRetry(() => import("./pages/MyExams"));

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
          {/* STUDENT REVIEWS -- write / edit / withdraw the student's own public review (website only). */}
          <Route path="rate-scoram" element={<RateScoram />} />
          {/* "MY EXAMS" -- select-exams is the first-run onboarding screen (AppLayout redirects
              here automatically for an authenticated student with zero exams configured);
              my-exams is the ongoing management screen, linked from Profile. */}
          <Route path="select-exams" element={<SelectExams />} />
          <Route path="my-exams" element={<MyExams />} />

          {/* GAMIFICATION -- all per-student, so behind the same auth gate as the rest of this
              block (GamificationController/ReferralsController on the backend both require
              a logged-in student too). */}
          <Route path="leaderboard" element={<Leaderboard />} />
          <Route path="progress" element={<ProgressPage />} />
          <Route path="progress/topic" element={<TopicProgress />} />
          <Route path="mistake-book" element={<MistakeBook />} />
          <Route path="mistake-book/revise" element={<Revision />} />
          <Route path="study-plan" element={<StudyPlan />} />
          <Route path="referrals" element={<Referrals />} />
          <Route path="study-partners" element={<StudyPartners />} />
        </Route>

        <Route path="*" element={<NotFound />} />
      </Route>

      <Route element={<AuthLayout />}>
        <Route path="login" element={<Login />} />
        <Route path="forgot-password" element={<ForgotPassword />} />
      </Route>

      {/* LANDING PAGE -- public support pages, own navbar/footer (LegalPage), no app chrome,
          so these are top-level routes rather than nested under AppLayout or AuthLayout. */}
      <Route element={<SuspenseOutlet />}>
        <Route path="our-team" element={<OurTeam />} />
        <Route path="privacy-policy" element={<PrivacyPolicy />} />
        <Route path="terms-and-conditions" element={<Terms />} />
        {/* "/terms" was the original URL for this page -- kept as a redirect so links already shared,
            indexed or printed keep working. */}
        <Route path="terms" element={<Navigate to="/terms-and-conditions" replace />} />
        {/* Public on purpose: this is the Google Play Data Safety "delete account URL". */}
        <Route path="delete-account" element={<DeleteAccount />} />
      </Route>
    </Routes>
  );
}

export default function App() {
  return (
    <AuthProvider>
      <MyExamsProvider>
        <ChatConnectionProvider>
          <AppRoutes />
        </ChatConnectionProvider>
      </MyExamsProvider>
    </AuthProvider>
  );
}
