import { Link } from "react-router-dom";
import LegalPage, { Fill } from "./LegalPage";
import { LEGAL } from "../config/legal";

// Every statement below was checked against the actual ScoramAPI / ScoramWeb / scoram_mobile code
// (see LEGAL_IMPLEMENTATION_NOTES.md for the evidence trail and the Play Data Safety mapping).
// If a feature, SDK or vendor is added or removed, update this page AND the Play Data Safety form
// together -- they must say the same thing.

const email = LEGAL.contactEmail;

const sections = [
  {
    id: "introduction",
    title: "Introduction",
    content: (
      <>
        <p>
          SCORAM is a competitive-exam preparation and learning platform, available as a website and an Android
          app. It offers previous year papers (PYP), a question bank (PYQ), tests, mock tests, quizzes,
          discussions, Study Partner, group chat, direct messages, bookmarks, progress tracking and a leaderboard.
        </p>
        <p>This Privacy Policy explains in plain language:</p>
        <ul>
          <li>what information SCORAM collects,</li>
          <li>how we use it,</li>
          <li>how we protect it and when we share it,</li>
          <li>how you can manage or delete your information, and</li>
          <li>how to contact us.</li>
        </ul>
        <p>
          It applies to the SCORAM website and the SCORAM Android app (together, the &ldquo;Service&rdquo;).
          &ldquo;We&rdquo;, &ldquo;us&rdquo; and &ldquo;SCORAM&rdquo; mean <Fill>{LEGAL.developerName}</Fill>, the developer
          of SCORAM. By using the Service you acknowledge this Policy; our{" "}
          <Link to={LEGAL.paths.terms}>Terms &amp; Conditions</Link> also apply.
        </p>
      </>
    ),
  },
  {
    id: "information-we-collect",
    title: "Information We Collect",
    content: (
      <>
        <h3>A. Account information</h3>
        <p>When you register we collect:</p>
        <ul>
          <li>your full name and a username,</li>
          <li>your email address,</li>
          <li>your mobile phone number, which you verify with a one-time password (OTP) sent by SMS, and</li>
          <li>a password. We never store your password itself &mdash; only a salted, one-way hash of it (bcrypt).</li>
        </ul>
        <p>
          We also generate an internal user ID, record when your account was created, and record when you last
          signed in. OTP codes are used only to verify your phone number (for registration, phone sign-in, changing
          your number, and confirming account deletion). We do not keep the OTP itself.
        </p>

        <h3>B. Profile information and preferences</h3>
        <ul>
          <li>An optional profile photo. On Android you can choose one from your gallery or take one with your device camera.</li>
          <li>The exams you select in <strong>My Exams</strong> (see Section 3).</li>
          <li>Your notification settings, and your Study Partner privacy settings (who can see your profile, progress and activity, whether you appear on the leaderboard, and whether you accept Study Partner requests).</li>
        </ul>

        <h3>C. Learning activity</h3>
        <p>To run the learning features and show you your own history, we store:</p>
        <ul>
          <li>tests, mock tests, quizzes and previous year paper attempts, including the answers you submit, scores and timing,</li>
          <li>questions you have solved, bookmarks you save, and likes, upvotes or downvotes you give,</li>
          <li>progress and gamification data: XP, level, streaks, badges and leaderboard standing,</li>
          <li>referral activity (the referral code you share or use, and the referrals that result), and</li>
          <li>Study Partner activity: requests, partnerships, blocks and challenges.</li>
        </ul>

        <h3>D. Content you create and messages</h3>
        <ul>
          <li><strong>Community content:</strong> discussion comments and replies, solutions you submit, group chat messages, polls and poll votes, and files you share in group chat.</li>
          <li><strong>Private messages:</strong> direct messages, including text, images, documents and voice notes you send.</li>
          <li><strong>Feedback and reports:</strong> feedback you send us (type, message, optional rating, and whether it came from web or app), and reports about questions, comments or chat messages (including an optional proof image).</li>
        </ul>
        <p>
          On the website, recording a voice note asks your browser for microphone permission, and only while you
          record. We do not access your microphone at any other time.
        </p>

        <h3>E. Device and technical information</h3>
        <ul>
          <li>
            <strong>Sign-in sessions:</strong> each time you sign in or your session is renewed, we store a session
            record that includes your <strong>IP address</strong> and the dates the session was created and expires.
          </li>
          <li>
            <strong>Push notification identifiers:</strong> on Android, a Firebase Cloud Messaging (FCM) device token
            together with the platform and app version; on the website, a browser push subscription, if you turn
            notifications on. These are used only to deliver notifications to your device.
          </li>
          <li>
            <strong>Server logs:</strong> our servers produce standard operational logs (such as request path, status and
            timing) to keep the Service reliable and to investigate problems and abuse.
          </li>
        </ul>

        <h3>What we do not collect</h3>
        <p>
          SCORAM does not collect your precise or approximate location, does not read your contacts, SMS or call
          history, and does not use advertising identifiers. The Android app does not request microphone, location or
          contacts permissions; its only declared permissions are internet access and showing notifications.
        </p>
      </>
    ),
  },
  {
    id: "my-exams",
    title: "My Exams",
    content: (
      <>
        <p>
          My Exams lets you choose the exams you are preparing for. It is a saved preference on your account, not a
          temporary filter. SCORAM uses it to personalize your experience, including:
        </p>
        <ul>
          <li>which previous year papers (PYP) and Question Bank / PYQ content you see,</li>
          <li>which tests, mock tests and quizzes are shown to you,</li>
          <li>relevant discussions and group content, and</li>
          <li>new-content notifications for the exams you chose.</li>
        </ul>
        <p>You can change your selections at any time from the My Exams screen; deleting your account removes them.</p>
      </>
    ),
  },
  {
    id: "how-we-use-information",
    title: "How We Use Information",
    content: (
      <>
        <p>We use the information described above to:</p>
        <ul>
          <li>create and manage your account, and verify and authenticate you,</li>
          <li>provide PYP, Question Bank, tests, mock tests and quizzes, and keep your attempt history and progress,</li>
          <li>personalize content using My Exams,</li>
          <li>run discussions, Study Partner, group chat, direct messages, bookmarks, the leaderboard and referrals,</li>
          <li>send notifications you have enabled,</li>
          <li>respond to feedback and support requests,</li>
          <li>detect and prevent abuse, spam, fraud and security incidents, and moderate content,</li>
          <li>maintain, troubleshoot and improve the reliability of the Service, and</li>
          <li>comply with legal obligations.</li>
        </ul>
        <p>
          SCORAM does not show third-party advertising and does not sell your personal information or use it for
          advertising.
        </p>
      </>
    ),
  },
  {
    id: "push-notifications",
    title: "Push Notifications",
    content: (
      <>
        <p>
          SCORAM uses Firebase Cloud Messaging (Android) and browser Web Push (website) to deliver notifications such
          as:
        </p>
        <ul>
          <li>direct messages, @mentions and group chat activity,</li>
          <li>replies to your discussion comments,</li>
          <li>Study Partner requests, acceptances and challenges, and quiz challenges,</li>
          <li>new tests, mock tests, previous year papers and quizzes for your exams,</li>
          <li>achievements, and important SCORAM announcements.</li>
        </ul>
        <p>
          To do this the app stores an FCM device token for your account (the website stores a push subscription). The
          token is a technical identifier used only to route notifications; it is not shown to you or to other users,
          and it is removed when you log out of that device or delete your account.
        </p>
        <p>
          You can turn notifications off in Android system settings, in your browser settings, or turn group and
          direct-message notifications off from your Profile / Settings in SCORAM.
        </p>
      </>
    ),
  },
  {
    id: "how-we-share-information",
    title: "How We Share Information",
    content: (
      <>
        <p>We do not sell your personal information. We share it only as described here.</p>

        <h3>A. Service providers</h3>
        <p>
          We use the following providers to run SCORAM. They process information only as needed to provide their
          service to us, under their own terms and privacy policies.
        </p>
        <div className="legal-table-wrap">
          <table>
            <thead>
              <tr>
                <th>Provider</th>
                <th>Purpose</th>
                <th>Information involved</th>
              </tr>
            </thead>
            <tbody>
              <tr>
                <td>MSG91</td>
                <td>Sends and verifies OTP SMS for registration, phone sign-in, changing your phone number and account deletion</td>
                <td>Your mobile number and the OTP verification</td>
              </tr>
              <tr>
                <td>Google (Firebase Cloud Messaging)</td>
                <td>Delivers push notifications to the Android app</td>
                <td>FCM device token and the notification content; Firebase may also process app/device identifiers under Google&apos;s terms</td>
              </tr>
              <tr>
                <td>Browser push services (run by Google, Mozilla, Apple, Microsoft and others)</td>
                <td>Delivers Web Push notifications, only if you enable them</td>
                <td>Your browser&apos;s push subscription and the notification content</td>
              </tr>
              <tr>
                <td>Microsoft Azure Blob Storage</td>
                <td>Stores files you upload (profile photos, chat and direct-message attachments, proof images, question images)</td>
                <td>The uploaded files</td>
              </tr>
              <tr>
                <td>Render</td>
                <td>Hosts SCORAM&apos;s application servers</td>
                <td>All data processed by the Service passes through these servers</td>
              </tr>
              <tr>
                <td>Google (Fonts and reCAPTCHA)</td>
                <td>The website loads fonts from Google Fonts, and the OTP step on the website uses MSG91&apos;s widget, which uses Google reCAPTCHA to deter bots. The Android app may also fetch fonts from Google Fonts.</td>
                <td>Technical data such as your IP address and browser details, as in any web request</td>
              </tr>
            </tbody>
          </table>
        </div>
        <p>
          Our database is a Microsoft SQL Server database. Service providers may process information in countries
          other than your own.
        </p>

        <h3>B. Other users and administrators</h3>
        <ul>
          <li>
            <strong>Public to other SCORAM users:</strong> your name, username and profile photo where they appear with
            your content (discussion comments, group chat, leaderboard, Study Partner search), your group chat
            messages, and whether you are online. What other users see of your profile, progress and activity is
            controlled by your Study Partner privacy settings.
          </li>
          <li>
            <strong>Private messages</strong> are visible only to you and the person you message, and to authorized
            SCORAM administrators only where needed to handle a report, abuse or a legal request.
          </li>
          <li>
            Authorized SCORAM administrators can see account details, feedback and reports in order to provide
            support, moderate content and keep the Service safe.
          </li>
        </ul>

        <h3>C. Legal requirements</h3>
        <p>
          We may disclose information if required by applicable law, legal process, a court order or a lawful request
          from a government authority, or where reasonably necessary to protect the rights, safety or security of
          SCORAM, our users or others.
        </p>

        <h3>D. Business transfers</h3>
        <p>
          If SCORAM is involved in a merger, acquisition, restructuring or sale of assets, information may be
          transferred as part of that transaction, subject to applicable law and appropriate protections.
        </p>
      </>
    ),
  },
  {
    id: "data-security",
    title: "Data Security",
    content: (
      <>
        <p>We use reasonable technical and organizational measures to protect your information, including:</p>
        <ul>
          <li>HTTPS (encryption in transit) between the apps and our servers,</li>
          <li>salted bcrypt hashing of passwords, so we cannot read your password,</li>
          <li>short-lived sign-in tokens, with renewable sessions that are rotated and stored only as hashes, and the ability to end all sessions,</li>
          <li>rate limiting and a captcha on sign-in and registration to slow brute-force and bot attacks,</li>
          <li>role-based access, so students cannot reach administrative functions, and extra verification for administrator accounts,</li>
          <li>uploaded files kept in a private storage container and served only through our application, and</li>
          <li>on Android, sign-in tokens kept in the device&apos;s secure storage.</li>
        </ul>
        <p>
          No method of transmission over the internet or of electronic storage is 100% secure, so we cannot guarantee
          absolute security. Please keep your password private and tell us right away if you suspect unauthorized use
          of your account.
        </p>
      </>
    ),
  },
  {
    id: "data-retention",
    title: "Data Retention",
    content: (
      <>
        <p>We keep information only for as long as reasonably necessary to:</p>
        <ul>
          <li>provide the Service and keep your account, history and progress working,</li>
          <li>meet legal or compliance requirements and resolve disputes,</li>
          <li>prevent fraud, abuse and security incidents, and</li>
          <li>run the Service reliably.</li>
        </ul>
        <p>In practice:</p>
        <ul>
          <li>Your account and learning data are kept while your account exists.</li>
          <li>Sign-in session records (including the IP address) are kept with your account and are erased when you delete it. A session currently stays valid for up to 30 days unless it is renewed.</li>
          <li>Server log files are rotated daily and kept for a short period (currently up to 14 daily files). Our hosting providers may keep their own logs under their own policies.</li>
          <li>When you delete your account, the data described in Section 9 is erased or anonymized right away. If our database provider keeps routine backups, deleted data may remain in those backups until they are overwritten in the normal course.</li>
        </ul>
        <p>We do not currently promise a fixed number of days or years beyond the above, and we do not keep data longer than we need it.</p>
      </>
    ),
  },
  {
    id: "account-deletion",
    title: "Account Deletion and Data Deletion",
    content: (
      <>
        <p>You can delete your SCORAM account and associated data yourself at any time.</p>

        <h3>How to delete your account</h3>
        <ul>
          <li><strong>In the Android app:</strong> Profile &rarr; Account &amp; Security &rarr; Delete Account.</li>
          <li><strong>On the website:</strong> sign in, open Settings, and use Delete Account. See also <Link to={LEGAL.paths.deleteAccount}>the Delete Account page</Link>.</li>
          <li><strong>If you cannot sign in:</strong> email <a href={`mailto:${email}`}>{email}</a> from the address registered on the account, using the steps on the Delete Account page. This route is handled manually.</li>
        </ul>
        <p>
          To protect your account, you must confirm by typing DELETE and prove it is you with your current password or a
          one-time code sent to your registered phone number.
        </p>

        <h3>What happens when you delete</h3>
        <p><strong>Erased permanently:</strong></p>
        <ul>
          <li>your test, mock test, quiz and PYP attempts and answers, XP, streaks, badges and solved-question history,</li>
          <li>your bookmarks, My Exams selections, feedback, notifications, and votes,</li>
          <li>your device (FCM) tokens and web push subscriptions, and your sign-in session records including IP addresses,</li>
          <li>your group memberships, Study Partner requests, partnerships, blocks, challenges and privacy settings, and quiz challenges,</li>
          <li>your profile photo and other files you uploaded (such as proof images and shared attachments), and</li>
          <li>the content of the direct messages you sent &mdash; the other person will see &ldquo;Message deleted&rdquo;.</li>
        </ul>
        <p><strong>Anonymized, not erased:</strong></p>
        <ul>
          <li>
            Text you posted in group chat, your discussion comments and submitted solutions, your poll votes, referral
            records and reports you filed stay so that conversations and community content still make sense. They are no
            longer linked to you: your name, username, email and phone number are replaced, and they appear as
            &ldquo;Deleted User&rdquo;. Text you wrote can still contain personal details if you typed them yourself.
          </li>
        </ul>
        <p>
          Your username, email address and phone number become available for registration again. Deletion cannot be
          undone, and we cannot restore a deleted account.
        </p>
        <p className="legal-callout">
          We may need to keep limited information where the law requires it or where needed to handle a security or
          abuse matter. We do not currently hold any such records beyond the anonymized items above.
        </p>
      </>
    ),
  },
  {
    id: "childrens-privacy",
    title: "Children's Privacy",
    content: (
      <>
        <p>
          SCORAM is built for students and competitive-exam aspirants. It is not designed for young children, and we do
          not knowingly collect personal information from children in violation of applicable law.
        </p>
        <p>
          Please provide accurate information when you register. If you are a minor, use SCORAM with the involvement of
          a parent or guardian where your local law requires it. If you believe a child has given us personal
          information in a way the law does not allow, contact us at <a href={`mailto:${email}`}>{email}</a> and we will
          delete it.
        </p>
      </>
    ),
  },
  {
    id: "third-party-services",
    title: "Third-Party Services and Links",
    content: (
      <>
        <p>
          SCORAM relies on the third-party services listed in Section 6. They have their own privacy policies and terms,
          and we encourage you to read them. SCORAM is responsible for how it handles your information; it is not
          responsible for the independent practices of those providers.
        </p>
        <p>
          Questions, discussions or messages may contain links to other websites. We do not control those sites or
          their privacy practices.
        </p>
      </>
    ),
  },
  {
    id: "user-generated-content",
    title: "User-Generated Content",
    content: (
      <>
        <p>
          SCORAM lets you post discussion comments and replies, solutions, group chat messages, polls, profile
          information, Study Partner interactions, direct messages and feedback. You are responsible for what you
          choose to submit.
        </p>
        <ul>
          <li><strong>Public/community content</strong> (discussions, solutions, group chat, leaderboard, your public profile details) can be seen by other SCORAM users.</li>
          <li><strong>Direct messages</strong> are private between you and the other person. They are not published.</li>
        </ul>
        <p>
          We may moderate or remove content that breaks our <Link to={LEGAL.paths.terms}>Terms &amp; Conditions</Link> or
          platform rules. Please do not post personal information about yourself or others that you do not want shared.
        </p>
      </>
    ),
  },
  {
    id: "cookies-local-storage",
    title: "Cookies and Local Storage",
    content: (
      <>
        <p>SCORAM does not set advertising cookies and does not use analytics or tracking cookies. On the website we use your browser&apos;s storage to make the app work:</p>
        <ul>
          <li><strong>Local storage:</strong> your sign-in session tokens and a cached copy of your basic profile (to keep you signed in), your sidebar layout choice, and your recent searches in the Question Bank.</li>
          <li><strong>Session storage:</strong> a short-lived flag so we do not repeatedly prompt you to choose exams in one browsing session.</li>
        </ul>
        <p>
          This storage is needed for sign-in and core features. You can clear it in your browser settings, which will
          sign you out. Third-party components on the website, such as the OTP widget and Google reCAPTCHA, may set
          their own cookies under their own policies. The Android app stores sign-in tokens in the device&apos;s secure
          storage.
        </p>
      </>
    ),
  },
  {
    id: "changes",
    title: "Changes to This Privacy Policy",
    content: (
      <>
        <p>
          We may update this Policy when our features, legal requirements, data practices or security practices change.
          The effective date and last-updated date at the top of this page show the current version. Where a change is
          material, we will tell you through appropriate means, for example a notice in the app or on the website,
          where required.
        </p>
      </>
    ),
  },
  {
    id: "contact",
    title: "Contact Us",
    content: (
      <>
        <p>For privacy questions, to ask about your data, or to request deletion:</p>
        <p>
          <strong>SCORAM</strong>
          <br />
          Developer: <Fill>{LEGAL.developerName}</Fill>
          <br />
          Email: <a href={`mailto:${email}`}>{email}</a>
        </p>
      </>
    ),
  },
];

export default function PrivacyPolicy() {
  return (
    <LegalPage
      title="Privacy Policy"
      subtitle="SCORAM – Learn | Discuss | Score"
      seoTitle="SCORAM Privacy Policy"
      description="Learn how SCORAM collects, uses, protects, retains, and deletes user information."
      path={LEGAL.paths.privacy}
      sections={sections}
    />
  );
}
