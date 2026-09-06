import LegalPage from "./LegalPage";

// Drafted from what SCORAM's own backend actually collects and where it actually sends data
// (Azure Blob Storage for uploaded files, browser push services for Web Push) -- not a generic
// template. Still a draft: have it reviewed by a lawyer before treating it as binding, and update
// the "Third-Party Services" section if a new vendor (payments, analytics, etc.) is ever added.
export default function PrivacyPolicy() {
  return (
    <LegalPage title="Privacy Policy" path="/privacy-policy" updated="September 6, 2026">
      <p>
        This Privacy Policy explains what information SCORAM ("we", "our", "us") collects when you
        use our website and apps (the "Service"), why we collect it, and the choices you have.
      </p>

      <h2>1. Information We Collect</h2>
      <p><strong>Account information you give us:</strong></p>
      <ul>
        <li>Full name, username, email address, and phone number, when you register</li>
        <li>Your password (we only ever store a securely hashed version, never the plain text)</li>
        <li>A profile photo, if you choose to upload one</li>
      </ul>
      <p><strong>Content you create on SCORAM:</strong></p>
      <ul>
        <li>Group Chat and direct messages, comments, question solutions, and question reports you post</li>
        <li>Your exam preferences ("My Exams"), bookmarks, and votes</li>
      </ul>
      <p><strong>Your learning &amp; activity data:</strong></p>
      <ul>
        <li>Test, Quiz, Mock Test, and Previous Year Paper attempts and scores</li>
        <li>XP, levels, streaks, badges, leaderboard standing, and referral activity</li>
        <li>Syllabus progress and typing test results, where you use those features</li>
      </ul>
      <p><strong>Technical data, collected automatically:</strong></p>
      <ul>
        <li>Standard server access logs (e.g. IP address, browser type, timestamps) generated whenever any website is used</li>
        <li>Your push-notification subscription, only if you turn on browser push notifications</li>
      </ul>

      <h2>2. Why We Use It</h2>
      <ul>
        <li>To create and secure your account, and let you log in</li>
        <li>To run the features you use — Mock Tests, PYPs, Quizzes, Question Bank, Group Chat, Discussions, Leaderboard</li>
        <li>To personalize what you see by default, based on the exams you've selected under "My Exams"</li>
        <li>To track your own progress: scores, XP, streaks, badges, and syllabus completion</li>
        <li>To send you notifications you've asked for (e.g. chat messages, when enabled)</li>
        <li>To maintain the security of the Service and diagnose technical problems</li>
        <li>To communicate with you about your account or important changes to the Service</li>
      </ul>

      <h2>3. Third-Party Services</h2>
      <p>We don't sell your personal information. We currently share data with:</p>
      <ul>
        <li><strong>Microsoft Azure Blob Storage</strong> — stores files you upload, such as your profile photo and chat/message attachments</li>
        <li><strong>Your browser's push notification service</strong> (run by your browser vendor, e.g. Google, Mozilla, or Apple) — used only to deliver Web Push notifications, and only if you enable them</li>
      </ul>
      <p>
        We do not currently use any third-party advertising or analytics tools. If that changes —
        for example, if we add payments or analytics in the future — we'll update this policy
        before doing so.
      </p>
      <p>We may also disclose information if required to by law, or to protect the rights, safety, or security of SCORAM or our users.</p>

      <h2>4. How Long We Keep Your Data</h2>
      <p>
        We keep your account and activity data for as long as your account is active, so your
        progress, streaks, and history stay intact. If you ask us to delete your account (see
        below), we'll delete or anonymize your personal information within a reasonable time,
        except where we're required to retain certain records by law.
      </p>

      <h2>5. Your Choices &amp; Rights</h2>
      <ul>
        <li><strong>Access &amp; update:</strong> you can view and edit your name, username, email, phone number, and photo, and change your password, from Settings in the app</li>
        <li><strong>Delete:</strong> you can request deletion of your account and personal data at any time by emailing us at the address below</li>
        <li><strong>Notifications:</strong> you can turn group and direct message notifications on or off from Settings</li>
      </ul>

      <h2>6. Contact</h2>
      <p>
        Questions about this policy, or requests to access, correct, or delete your data, can be
        sent to info@scoram.in.
      </p>

      <p className="text-sm text-ink-400">
        This is a working draft that reflects what SCORAM actually collects today. We'd recommend
        having it reviewed by a qualified lawyer before relying on it as your final, binding
        Privacy Policy.
      </p>
    </LegalPage>
  );
}
