// Single source of truth for the permission catalogue shown in Manage Admins and the Roles & Permissions
// matrix. Keys MUST match the backend AdminPermission enum names (stored as strings). The server is the real
// authority: this list only drives checkboxes and labels.
export const PERMISSIONS = [
  { key: "UploadPaper", group: "Content", label: "Upload Paper", hint: "Create exams and upload new PYQ papers/questions" },
  { key: "EditPaper", group: "Content", label: "Edit Paper", hint: "Edit questions in a Draft paper" },
  { key: "DeletePaper", group: "Content", label: "Delete Paper", hint: "Delete papers and individual questions" },
  { key: "PublishPaper", group: "Content", label: "Publish Paper", hint: "Approve/reject/publish/unpublish -- also skips review when this admin submits their own uploads" },
  { key: "ManageQuestionBank", group: "Content", label: "Manage Question Bank", hint: "Add/edit/delete Question Bank questions, bulk Excel/JSON import, manage Subjects and Topics" },
  { key: "ManageSubjects", group: "Content", label: "Manage Subjects", hint: "Rename, activate/deactivate, merge, reassign and delete Subjects (master data that every PYQ/PYP/test filter uses), and manage each exam's own subject/topic list and question mapping" },
  { key: "ManageTests", group: "Content", label: "Manage Tests", hint: "Create/edit/publish/schedule Mock Tests and Practice Test templates, view student attempts and results" },
  { key: "ModerateSolutions", group: "Moderation", label: "Moderate Solutions", hint: "Approve/reject student-submitted solutions, and post Official/Teacher solutions" },
  { key: "ModerateDiscussions", group: "Moderation", label: "Moderate Discussions", hint: "Pin comments, mark reported comments resolved/removed, and post official replies" },
  { key: "ModerateQuestionReports", group: "Moderation", label: "Moderate Question Reports", hint: "Review \"Report Question\" submissions on PYQ and Question Bank questions" },
  { key: "HandleChatReports", group: "Moderation", label: "Handle Chat Reports", hint: "Review and resolve reported chat messages" },
  { key: "ManageBannedWords", group: "Moderation", label: "Manage Banned Words", hint: "Maintain the list of words/phrases blocked from chat messages" },
  { key: "ModerateReviews", group: "Moderation", label: "Moderate Student Reviews", hint: "Approve, reject, hide or remove the public \"Rate SCORAM\" reviews students submit on the website" },
  { key: "ManageChatRooms", group: "Community", label: "Manage Chat Rooms", hint: "Create/rename/delete standalone group chat rooms (not tied to an exam)" },
  { key: "ToggleChatLock", group: "Community", label: "Lock/Unlock Chat", hint: "Disable or re-enable posting in a room without deleting its history" },
  { key: "RemoveGroupMembers", group: "Community", label: "Remove Group Members", hint: "Remove/ban a student from a chat room" },
  { key: "PostNotices", group: "Community", label: "Post Notices & Notifications", hint: "Post chat notices and send push / in-app announcements to students (Notifications center)" },
  { key: "CreatePolls", group: "Community", label: "Create Polls", hint: "Post a poll into a chat room" },
  { key: "ManageFeedback", group: "Support", label: "Manage Feedback & Support", hint: "View student feedback / support tickets, assign them, add internal notes and reply to students" },
  { key: "ManageUsers", group: "Platform", label: "Manage Students", hint: "Search students, view their details and sessions, suspend / reactivate accounts, sign them out" },
  { key: "ViewAnalytics", group: "Platform", label: "View Analytics", hint: "Open the Analytics page, user and learning metrics, and System Health" },
  { key: "ExportReports", group: "Platform", label: "Export Reports", hint: "Download CSV / Excel reports (each report also needs the permission for its data)" },
  { key: "ManageAppConfig", group: "Platform", label: "Manage App Configuration", hint: "Feature flags, maintenance mode, app version policy and the config change history. Critical settings stay Super Admin only." },
  { key: "ManageLandingContent", group: "Platform", label: "Manage Landing Page", hint: "Replace landing-page images (logo, feature images, founder photos) and edit the Smart Progress section" },
  { key: "Audit", group: "Platform", label: "Audit", hint: "View the audit log of admin activity (publishes, rejections, deletions, permission changes)" },
];

export const PERMISSION_GROUPS = ["Content", "Moderation", "Community", "Support", "Platform"];

// Convenience starting points for the permission editor. They only pre-tick boxes -- a Super Admin still
// reviews and saves, and the server validates every key. "Super Admin" is a role, not a preset.
export const ROLE_PRESETS = [
  { name: "Content editor", keys: ["UploadPaper", "EditPaper", "ManageQuestionBank", "ManageSubjects"] },
  { name: "Reviewer / publisher", keys: ["UploadPaper", "EditPaper", "PublishPaper", "DeletePaper", "ModerateQuestionReports"] },
  { name: "Moderator", keys: ["ModerateSolutions", "ModerateDiscussions", "ModerateQuestionReports", "HandleChatReports", "ManageBannedWords", "RemoveGroupMembers", "ToggleChatLock"] },
  { name: "Support agent", keys: ["ManageFeedback", "ManageUsers"] },
  { name: "Analyst", keys: ["ViewAnalytics", "ExportReports"] },
  { name: "Test manager", keys: ["ManageTests", "ManageQuestionBank"] },
];
