import { Routes, Route } from "react-router-dom";
import { lazyWithRetry } from "../components/routing/lazyWithRetry";
import { AdminAuthProvider } from "./context/AdminAuthContext";
import AdminLayout from "./layouts/AdminLayout";
import AdminProtectedRoute from "./routes/AdminProtectedRoute";
import RequireAdminPermission from "./routes/RequireAdminPermission";
import AdminLogin from "./pages/AdminLogin";
import AdminChangePassword from "./pages/AdminChangePassword";
import AdminNotFound from "./pages/AdminNotFound";

// Admin pages are split per route (same pattern as the student App.jsx): the shell, guards and the
// login/not-found screens load with the admin chunk; every management page downloads on first use.
// Permission guards (RequireAdminPermission) are unchanged and still wrap the lazy elements.
const AdminMfaSettings = lazyWithRetry(() => import("./pages/AdminMfaSettings"));
const AdminDashboard = lazyWithRetry(() => import("./pages/AdminDashboard"));
const PyqUploadWizard = lazyWithRetry(() => import("./pages/PyqUploadWizard"));
const BulkPaperUpload = lazyWithRetry(() => import("./pages/BulkPaperUpload"));
const UploadedPapers = lazyWithRetry(() => import("./pages/UploadedPapers"));
const PaperDetailView = lazyWithRetry(() => import("./pages/PaperDetailView"));
const ReviewQueue = lazyWithRetry(() => import("./pages/ReviewQueue"));
const ChatModeration = lazyWithRetry(() => import("./pages/ChatModeration"));
const ExamManagement = lazyWithRetry(() => import("./pages/ExamManagement"));
const OrganizationManagement = lazyWithRetry(() => import("./pages/OrganizationManagement"));
const TaskManagement = lazyWithRetry(() => import("./pages/TaskManagement"));
const ManageAdmins = lazyWithRetry(() => import("./pages/ManageAdmins"));
const AuditLog = lazyWithRetry(() => import("./pages/AuditLog"));
const SolutionsQueue = lazyWithRetry(() => import("./pages/SolutionsQueue"));
const CommentReportsQueue = lazyWithRetry(() => import("./pages/CommentReportsQueue"));
const QuestionBankManagement = lazyWithRetry(() => import("./pages/QuestionBankManagement"));
const QuestionBankUploadWizard = lazyWithRetry(() => import("./pages/QuestionBankUploadWizard"));
const TestBulkUpload = lazyWithRetry(() => import("./pages/TestBulkUpload"));
const QuestionBankSubjectsTopics = lazyWithRetry(() => import("./pages/QuestionBankSubjectsTopics"));
const SubjectManagement = lazyWithRetry(() => import("./pages/SubjectManagement"));
const ExamTaxonomyManagement = lazyWithRetry(() => import("./pages/ExamTaxonomyManagement"));
const QuestionBankReportsQueue = lazyWithRetry(() => import("./pages/QuestionBankReportsQueue"));
const MockTestManagement = lazyWithRetry(() => import("./pages/MockTestManagement"));
const PracticeTestManagement = lazyWithRetry(() => import("./pages/PracticeTestManagement"));
const QuizManagement = lazyWithRetry(() => import("./pages/QuizManagement"));
const FeedbackManagement = lazyWithRetry(() => import("./pages/FeedbackManagement"));
const AdminSecuritySettings = lazyWithRetry(() => import("./pages/AdminSecuritySettings"));
const SharedContent = lazyWithRetry(() => import("./pages/SharedContent"));
const BulkQuestionEditor = lazyWithRetry(() => import("./pages/BulkQuestionEditor"));
const AnalyticsPage = lazyWithRetry(() => import("./pages/AnalyticsPage"));
const UsersManagement = lazyWithRetry(() => import("./pages/UsersManagement"));
const ReportsExports = lazyWithRetry(() => import("./pages/ReportsExports"));
const NotificationCenter = lazyWithRetry(() => import("./pages/NotificationCenter"));
const AppConfiguration = lazyWithRetry(() => import("./pages/AppConfiguration"));
const AdminSessions = lazyWithRetry(() => import("./pages/AdminSessions"));
const SupportTickets = lazyWithRetry(() => import("./pages/SupportTickets"));
const SystemHealth = lazyWithRetry(() => import("./pages/SystemHealth"));
const ModerationOverview = lazyWithRetry(() => import("./pages/ModerationOverview"));
const RolesPermissions = lazyWithRetry(() => import("./pages/RolesPermissions"));
const StudentReviews = lazyWithRetry(() => import("./pages/StudentReviews"));
const LandingContentManagement = lazyWithRetry(() => import("./pages/LandingContentManagement"));

function AdminRoutes() {
  return (
    <Routes>
      <Route path="login" element={<AdminLogin />} />

      <Route element={<AdminProtectedRoute />}>
        <Route path="change-password" element={<AdminChangePassword />} />

        <Route element={<AdminLayout />}>
          <Route index element={<AdminDashboard />} />
          <Route path="upload" element={<PyqUploadWizard />} />
          <Route path="upload/bulk-papers" element={<BulkPaperUpload />} />
          <Route path="papers" element={<UploadedPapers />} />
          <Route path="papers/:paperId" element={<PaperDetailView />} />
          <Route path="chat" element={<ChatModeration />} />
          <Route path="exams" element={<ExamManagement />} />
          <Route path="organizations" element={<OrganizationManagement />} />
          <Route path="tasks" element={<TaskManagement />} />
          <Route path="mfa-settings" element={<AdminMfaSettings />} />
          {/* Voluntary password change -- any authenticated admin, own account only.
              Backend authorisation is the real boundary (see AdminAuthController.ChangePassword). */}
          <Route path="security" element={<AdminSecuritySettings />} />

          <Route element={<RequireAdminPermission permission="PublishPaper" />}>
            <Route path="review" element={<ReviewQueue />} />
          </Route>

          {/* Shared passages/tables/images + multi-paper bulk editor (server enforces EditPaper/UploadPaper) */}
          <Route element={<RequireAdminPermission permission="EditPaper" />}>
            <Route path="shared-content" element={<SharedContent />} />
            <Route path="bulk-editor" element={<BulkQuestionEditor />} />
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

          {/* Subject Management: master-data admin (rename / merge / reassign / delete Subjects) */}
          <Route element={<RequireAdminPermission permission="ManageSubjects" />}>
            <Route path="subjects" element={<SubjectManagement />} />
            {/* Exam-wise Subject & Topic Management: per-exam rename / add / delete + question mapping */}
            <Route path="exam-taxonomy" element={<ExamTaxonomyManagement />} />
          </Route>

          {/* STUDENT REVIEWS: moderation of the public "Rate SCORAM" reviews */}
          <Route element={<RequireAdminPermission permission="ModerateReviews" />}>
            <Route path="reviews" element={<StudentReviews />} />
          </Route>

          {/* LANDING PAGE: replaceable landing images + Smart Progress section copy */}
          <Route element={<RequireAdminPermission permission="ManageLandingContent" />}>
            <Route path="landing-content" element={<LandingContentManagement />} />
          </Route>

          {/* USER FEEDBACK: submissions from the floating Feedback button on the student app */}
          <Route element={<RequireAdminPermission permission="ManageFeedback" />}>
            <Route path="feedback" element={<FeedbackManagement />} />
          </Route>

          {/* SCORAM_TESTS */}
          <Route element={<RequireAdminPermission permission="ManageTests" />}>
            <Route path="mock-tests" element={<MockTestManagement />} />
            <Route path="practice-tests" element={<PracticeTestManagement />} />
            <Route path="test-import/:kind" element={<TestBulkUpload />} />
            <Route path="quizzes" element={<QuizManagement />} />
          </Route>

          <Route element={<RequireAdminPermission superAdminOnly />}>
            <Route path="admins" element={<ManageAdmins />} />
            <Route path="roles" element={<RolesPermissions />} />
            <Route path="sessions" element={<AdminSessions />} />
          </Route>

          {/* ADMIN PLATFORM -- every route below is mirrored by a server-side permission check */}
          <Route element={<RequireAdminPermission permission="ViewAnalytics" />}>
            <Route path="analytics" element={<AnalyticsPage />} />
            <Route path="system-health" element={<SystemHealth />} />
          </Route>
          <Route element={<RequireAdminPermission permission="ManageUsers" />}>
            <Route path="users" element={<UsersManagement />} />
          </Route>
          <Route element={<RequireAdminPermission permission="ExportReports" />}>
            <Route path="reports" element={<ReportsExports />} />
          </Route>
          <Route element={<RequireAdminPermission permission="PostNotices" />}>
            <Route path="notifications" element={<NotificationCenter />} />
          </Route>
          <Route element={<RequireAdminPermission permission="ManageAppConfig" />}>
            <Route path="app-config" element={<AppConfiguration />} />
          </Route>
          <Route element={<RequireAdminPermission permission="ManageFeedback" />}>
            <Route path="support" element={<SupportTickets />} />
          </Route>
          <Route element={<RequireAdminPermission anyOf={["PublishPaper", "ModerateQuestionReports", "ModerateSolutions", "ModerateDiscussions", "HandleChatReports"]} />}>
            <Route path="moderation" element={<ModerationOverview />} />
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
