import { Link } from "react-router-dom";
import LegalPage, { Fill } from "./LegalPage";
import { LEGAL } from "../config/legal";

// Features named here were checked against the codebase (see LEGAL_IMPLEMENTATION_NOTES.md).
// Payments: SCORAM has NO payment or subscription code today, so Section 10 deliberately does not
// invent pricing/refund terms -- add real ones when paid features actually ship.

const email = LEGAL.contactEmail;

const sections = [
  {
    id: "acceptance",
    title: "Acceptance of Terms",
    content: (
      <>
        <p>
          These Terms &amp; Conditions (&ldquo;Terms&rdquo;) govern your access to and use of SCORAM &mdash; the website and the
          Android app (together, the &ldquo;Service&rdquo;), provided by <Fill>{LEGAL.developerName}</Fill>. By accessing or
          using SCORAM, or by creating an account, you agree to these Terms and to our{" "}
          <Link to={LEGAL.paths.privacy}>Privacy Policy</Link>. If you do not agree, please do not use the Service.
        </p>
      </>
    ),
  },
  {
    id: "about",
    title: "About SCORAM",
    content: (
      <>
        <p>SCORAM provides educational and exam-preparation tools, including:</p>
        <ul>
          <li>Previous Year Papers (PYP) that you can attempt as timed papers,</li>
          <li>a Question Bank of previous-year questions (PYQ) with solutions,</li>
          <li>practice tests, mock tests and quizzes,</li>
          <li>discussions on questions, and group chat and direct messages,</li>
          <li>Study Partner, including partner challenges,</li>
          <li>progress tracking, XP, streaks, badges, a leaderboard and referrals,</li>
          <li>bookmarks, My Exams personalization and notifications.</li>
        </ul>
        <p>Features may change over time, and not every feature is available on every platform.</p>
      </>
    ),
  },
  {
    id: "eligibility-account",
    title: "Eligibility and Account",
    content: (
      <>
        <ul>
          <li>Provide accurate, current information when you register, including a phone number you can verify.</li>
          <li>You are responsible for keeping your password and account secure, and for activity under your account.</li>
          <li>Do not share your credentials where that is prohibited, create fraudulent or duplicate accounts to gain an advantage, or impersonate another person.</li>
          <li>If you are a minor, use SCORAM with the involvement of a parent or guardian where your local law requires it.</li>
        </ul>
        <p>You can delete your account at any time (see Section 12 and the <Link to={LEGAL.paths.deleteAccount}>Delete Account page</Link>).</p>
      </>
    ),
  },
  {
    id: "educational-disclaimer",
    title: "Educational Disclaimer",
    content: (
      <>
        <p>
          SCORAM is an educational preparation platform. Content is provided for learning and practice. SCORAM does not
          guarantee any exam selection, rank, marks, employment, admission, government job, qualification or other
          outcome of any examination.
        </p>
        <p>
          SCORAM is not an examination authority and is not affiliated with, endorsed by, or acting for any government
          body or examination conducting authority. You are responsible for verifying official notifications,
          eligibility, dates, syllabus, vacancies, application requirements and other official information with the
          relevant examination authority.
        </p>
      </>
    ),
  },
  {
    id: "content-accuracy",
    title: "Content Accuracy",
    content: (
      <>
        <p>We make reasonable efforts to provide useful, accurate content, but:</p>
        <ul>
          <li>errors can occur in questions, options, answers and solutions,</li>
          <li>exam patterns and syllabi change,</li>
          <li>official answer keys may differ from what appears on SCORAM, and</li>
          <li>reference material may be updated or become outdated.</li>
        </ul>
        <p>
          Please verify anything important with the official source. If you find a mistake, you can report a question
          from within the question screen (on the website or app), or write to <a href={`mailto:${email}`}>{email}</a>.
        </p>
      </>
    ),
  },
  {
    id: "intellectual-property",
    title: "Intellectual Property",
    content: (
      <>
        <p>
          SCORAM owns or licenses the SCORAM name, logo and branding, the software, user interface, platform design,
          database and original content (such as original solutions, explanations and tests created for SCORAM).
        </p>
        <p>
          Previous year question papers and similar exam material may originate from examination authorities or other
          third-party sources. SCORAM does not claim ownership of third-party exam papers or content; ownership remains with the
          respective rights holders where applicable.
        </p>
        <p>You may use SCORAM content for your own personal exam preparation. Without our written permission you must not:</p>
        <ul>
          <li>copy, reproduce, redistribute, republish or resell SCORAM material,</li>
          <li>scrape or bulk-download content, or use bots or automated tools against the Service,</li>
          <li>reverse engineer the software, except where the law allows it regardless of this restriction, or</li>
          <li>commercially exploit SCORAM or its content.</li>
        </ul>
        <p>
          You keep ownership of the content you post. By posting it on SCORAM you give us a non-exclusive, worldwide,
          royalty-free licence to host, display and distribute it within the Service so that it can work as intended
          (for example, showing your solution to other students). This licence ends when the content is deleted,
          except for copies that were already shared in the normal course of the Service or that we retain in
          anonymized form as described in the Privacy Policy.
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
          This applies to discussions, comments, solutions, group messages, direct messages, profile content, polls and
          feedback. You are responsible for what you submit. You must not submit:
        </p>
        <ul>
          <li>illegal content, or content that infringes someone else&apos;s rights, including copyrighted material you are not authorized to share,</li>
          <li>abusive or harassing content, threats, or hateful content,</li>
          <li>sexually explicit content, or any content that exploits or endangers minors,</li>
          <li>spam, scams, malware or malicious links,</li>
          <li>impersonation, fraudulent or deliberately misleading content, or</li>
          <li>content intended to deceive or harm others.</li>
        </ul>
        <p>
          We may remove content and restrict or suspend accounts that break these rules. Moderation may be automated
          (for example, a word filter in chat) or by an administrator.
        </p>
      </>
    ),
  },
  {
    id: "community-rules",
    title: "Messaging and Community Rules",
    content: (
      <>
        <p>In direct messages, group chat, discussions and Study Partner, communicate respectfully. Do not:</p>
        <ul>
          <li>harass, threaten, abuse or impersonate anyone,</li>
          <li>send spam, scams or unsolicited promotions,</li>
          <li>share malicious links or files, or</li>
          <li>use SCORAM for any illegal activity.</li>
        </ul>
        <p>
          <strong>Reporting and blocking.</strong> You can report group chat messages, discussion comments and
          questions from within the app or website, and you can block another user through Study Partner. Reports are
          sent to SCORAM administrators; we do not promise that every report will receive an individual review or a
          particular outcome, but we use reports to decide when to act.
        </p>
      </>
    ),
  },
  {
    id: "acceptable-use",
    title: "Acceptable Use",
    content: (
      <>
        <p>You must not:</p>
        <ul>
          <li>hack, probe or attack SCORAM or its infrastructure, or bypass authentication or security measures,</li>
          <li>access or try to access another person&apos;s account, or any administrator function or API,</li>
          <li>abuse or exploit our APIs, or automate requests in a way that burdens the Service,</li>
          <li>upload malicious files,</li>
          <li>interfere with other users&apos; use of the Service,</li>
          <li>manipulate tests, quizzes, progress, XP, streaks, referrals or the leaderboard, for example by cheating, sharing answers during a live attempt, or using multiple accounts, or</li>
          <li>misuse notifications or messaging.</li>
        </ul>
      </>
    ),
  },
  {
    id: "payments",
    title: "Payments and Subscriptions",
    content: (
      <>
        <p>
          SCORAM is currently offered without any paid plan, in-app purchase or subscription. If we introduce paid
          services in the future, they will be governed by additional pricing, billing, renewal, cancellation, refund
          and tax terms shown to you at the time of purchase.
        </p>
      </>
    ),
  },
  {
    id: "third-party-services",
    title: "Third-Party Services",
    content: (
      <>
        <p>
          SCORAM uses third-party infrastructure and services to operate, including MSG91 (OTP SMS), Google Firebase
          Cloud Messaging and browser push services (notifications), Microsoft Azure Blob Storage (file storage), and
          Render (hosting). Your use of the Service may also be subject to those providers&apos; own terms. Details of what
          they process are in the <Link to={LEGAL.paths.privacy}>Privacy Policy</Link>.
        </p>
      </>
    ),
  },
  {
    id: "suspension-termination",
    title: "Suspension and Termination",
    content: (
      <>
        <p>
          We may suspend or terminate an account, or restrict features, where reasonably appropriate &mdash; for example
          for a breach of these Terms, fraud, abuse, a security threat, illegal activity, or manipulation of the
          platform. Where the law requires notice or an opportunity to respond, we will follow it.
        </p>
        <p>
          You can stop using SCORAM and delete your account at any time. After suspension or termination you lose access
          to the account and to features that need it. Deleting your account permanently erases or anonymizes your
          data as described in Section 9 of the <Link to={LEGAL.paths.privacy}>Privacy Policy</Link>. Sections that by their
          nature should continue (such as intellectual property, disclaimers and limitation of liability) will
          continue to apply.
        </p>
      </>
    ),
  },
  {
    id: "availability",
    title: "Availability",
    content: (
      <>
        <p>
          We aim to keep SCORAM reliable, but we cannot guarantee uninterrupted or error-free service, zero downtime, or
          that every feature will always be available. Maintenance, outages, updates and technical issues may
          temporarily affect the Service, including timed attempts. We may change or discontinue features.
        </p>
      </>
    ),
  },
  {
    id: "limitation-of-liability",
    title: "Limitation of Liability",
    content: (
      <>
        <p>
          The Service is provided on an &ldquo;as is&rdquo; and &ldquo;as available&rdquo; basis. To the extent permitted by applicable
          law, SCORAM and its developer are not liable for indirect, incidental, special or consequential losses
          arising from your use of, or inability to use, the Service &mdash; including lost practice time, lost data or
          lost opportunities &mdash; and are not responsible for your results in any actual examination.
        </p>
        <p>
          Nothing in these Terms excludes or limits any liability or any of your rights that cannot be excluded or
          limited under applicable law, including consumer-protection rights and liability for fraud.
        </p>
      </>
    ),
  },
  {
    id: "changes",
    title: "Changes to These Terms",
    content: (
      <>
        <p>
          We may update these Terms as SCORAM changes. The effective date and last-updated date at the top of this page
          show the current version. Please review the Terms from time to time; continuing to use the Service after an
          update takes effect means you accept the updated Terms.
        </p>
      </>
    ),
  },
  {
    id: "governing-law",
    title: "Governing Law and Jurisdiction",
    content: (
      <>
        <p>
          These Terms are governed by: <Fill>{LEGAL.governingLaw}</Fill>
        </p>
      </>
    ),
  },
  {
    id: "contact",
    title: "Contact",
    content: (
      <>
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

export default function Terms() {
  return (
    <LegalPage
      title="SCORAM Terms & Conditions"
      subtitle="SCORAM – Learn | Discuss | Score"
      seoTitle="SCORAM Terms & Conditions"
      description="Read the terms governing access to and use of the SCORAM learning platform."
      path={LEGAL.paths.terms}
      sections={sections}
    />
  );
}
