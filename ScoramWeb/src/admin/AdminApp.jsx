import { Routes, Route } from "react-router-dom";
import { AdminAuthProvider } from "./context/AdminAuthContext";
import AdminLayout from "./layouts/AdminLayout";
import AdminProtectedRoute from "./routes/AdminProtectedRoute";
import RequireAdminPermission from "./routes/RequireAdminPermission";
import AdminLogin from "./pages/AdminLogin";
import AdminDashboard from "./pages/AdminDashboard";
import PyqUploadWizard from "./pages/PyqUploadWizard";
import UploadedPapers from "./pages/UploadedPapers";
import PaperDetailView from "./pages/PaperDetailView";
import ReviewQueue from "./pages/ReviewQueue";
import ChatModeration from "./pages/ChatModeration";
import ExamManagement from "./pages/ExamManagement";
import TaskManagement from "./pages/TaskManagement";
import ManageAdmins from "./pages/ManageAdmins";
import AuditLog from "./pages/AuditLog";
import SolutionsQueue from "./pages/SolutionsQueue";
import CommentReportsQueue from "./pages/CommentReportsQueue";
import QuestionBankManagement from "./pages/QuestionBankManagement";
import QuestionBankUploadWizard from "./pages/QuestionBankUploadWizard";
import QuestionBankSubjectsTopics from "./pages/QuestionBankSubjectsTopics";
import QuestionBankReportsQueue from "./pages/QuestionBankReportsQueue";
import MockTestManagement from "./pages/MockTestManagement";
import PracticeTestManagement from "./pages/PracticeTestManagement";
import QuizManagement from "./pages/QuizManagement";
import AdminNotFound from "./pages/AdminNotFound";

function AdminRoutes() {
  return (
    <Routes>
      <Route path="login" element={<AdminLogin />} />

      <Route element={<AdminProtectedRoute />}>
        <Route element={<AdminLayout />}>
          <Route index element={<AdminDashboard />} />
          <Route path="upload" element={<PyqUploadWizard />} />
          <Route path="papers" element={<UploadedPapers />} />
          <Route path="papers/:paperId" element={<PaperDetailView />} />
          <Route path="chat" element={<ChatModeration />} />
          <Route path="exams" element={<ExamManagement />} />
          <Route path="tasks" element={<TaskManagement />} />

          <Route element={<RequireAdminPermission permission="PublishPaper" />}>
            <Route path="review" element={<ReviewQueue />} />
          </Route>

          <Route element={<RequireAdminPermission permission="Audit" />}>
            <Route path="audit-log" element={<AuditLog />} />
          </Route>

          <Route element={<RequireAdminPermission permission="ModerateSolutions" />}>
            <Route path="solutions" element={<SolutionsQueue />} />
          </Route>

          <Route element={<RequireAdminPermission permission="ModerateDiscussions" />}>
            <Route path="comment-reports" element={<CommentReportsQueue />} />
          </Route>

          {/* SCORAM_QUESTION_BANK */}
          <Route element={<RequireAdminPermission permission="ManageQuestionBank" />}>
            <Route path="question-bank" element={<QuestionBankManagement />} />
            <Route path="question-bank/upload" element={<QuestionBankUploadWizard />} />
            <Route path="question-bank/subjects-topics" element={<QuestionBankSubjectsTopics />} />
          </Route>

          <Route element={<RequireAdminPermission permission="ModerateQuestionReports" />}>
            <Route path="question-bank/reports" element={<QuestionBankReportsQueue />} />
          </Route>

          {/* SCORAM_TESTS */}
          <Route element={<RequireAdminPermission permission="ManageTests" />}>
            <Route path="mock-tests" element={<MockTestManagement />} />
            <Route path="practice-tests" element={<PracticeTestManagement />} />
            <Route path="quizzes" element={<QuizManagement />} />
          </Route>

          <Route element={<RequireAdminPermission superAdminOnly />}>
            <Route path="admins" element={<ManageAdmins />} />
          </Route>

          <Route path="*" element={<AdminNotFound />} />
        </Route>
      </Route>
    </Routes>
  );
}

export default function AdminApp() {
  return (
    <AdminAuthProvider>
      <AdminRoutes />
    </AdminAuthProvider>
  );
}
