import { Link, useSearchParams } from "react-router-dom";
import { CheckCircle2 } from "lucide-react";
import LegalPage, { Fill } from "./LegalPage";
import DeleteAccountForm from "../components/auth/DeleteAccountForm";
import { useAuth } from "../context/AuthContext";
import { LEGAL } from "../config/legal";

// PUBLIC page (no login required to read it) -- this is the "delete account URL" for Google Play's
// Data Safety form. Honest about the two real routes: automated self-service deletion for anyone
// who can sign in, and a MANUAL email process for anyone who can't. Do not describe the email
// route as automated -- it is not.

function HowTo() {
  return (
    <>
      <h3>In the SCORAM Android app</h3>
      <ol>
        <li>Open <strong>Profile</strong>.</li>
        <li>Tap <strong>Account &amp; Security</strong>.</li>
        <li>Tap <strong>Delete Account</strong>, then confirm with your password (or a phone OTP) and type DELETE.</li>
      </ol>
      <h3>On the website</h3>
      <ol>
        <li>Sign in, then open <strong>Settings</strong> and choose <strong>Delete Account</strong> &mdash; or use the form below.</li>
        <li>Confirm with your password (or a phone OTP) and type DELETE.</li>
      </ol>
    </>
  );
}

function WebFlow() {
  const { isAuthenticated, user } = useAuth();
  const [params] = useSearchParams();

  if (params.get("deleted") === "1" && !isAuthenticated) {
    return (
      <div role="status" className="mt-4 flex items-start gap-3 rounded-xl2 border border-mint-100 bg-mint-50 p-4 text-ink-900">
        <CheckCircle2 className="mt-0.5 h-5 w-5 shrink-0 text-mint-500" strokeWidth={2.25} />
        <div>
          <p className="font-semibold">Your SCORAM account has been deleted.</p>
          <p className="mt-1 text-sm">
            Your personal data was erased or anonymized as described in the{" "}
            <Link to={`${LEGAL.paths.privacy}#account-deletion`}>Privacy Policy</Link>. You&apos;re welcome to create a new
            account any time.
          </p>
        </div>
      </div>
    );
  }

  if (!isAuthenticated) {
    return (
      <div className="legal-callout">
        <p className="!mt-0 font-semibold">Sign in to delete your account</p>
        <p>For your security we need to know it&apos;s really you before we delete anything.</p>
        <p>
          <Link
            to={`/login?redirect=${encodeURIComponent(LEGAL.paths.deleteAccount)}`}
            className="inline-block rounded-xl2 bg-primary-600 px-4 py-2 text-sm font-semibold !text-white no-underline hover:bg-primary-700 hover:!no-underline"
          >
            Sign in
          </Link>
        </p>
      </div>
    );
  }

  return (
    <>
      <p>
        You&apos;re signed in as <strong>{user?.fullName}</strong> (@{user?.username}).
      </p>
      <div className="mt-4">
        <DeleteAccountForm />
      </div>
    </>
  );
}

const sections = [
  {
    id: "delete-in-app",
    title: "Delete your account yourself",
    content: (
      <>
        <p>
          If you can sign in, you can delete your SCORAM account and associated data yourself, instantly, in the app
          or on the website.
        </p>
        <HowTo />
        <div className="mt-6">
          <WebFlow />
        </div>
      </>
    ),
  },
  {
    id: "what-happens",
    title: "What happens to your data",
    content: (
      <>
        <p><strong>Erased permanently:</strong></p>
        <ul>
          <li>test, mock test, quiz and PYP attempts and answers, XP, streaks, badges and solved-question history,</li>
          <li>bookmarks, My Exams selections, feedback, notifications and votes,</li>
          <li>device (push) tokens, web push subscriptions and sign-in session records including IP addresses,</li>
          <li>group memberships, Study Partner requests, partnerships, blocks, challenges and privacy settings,</li>
          <li>your profile photo and files you uploaded, and</li>
          <li>the content of the direct messages you sent (the other person sees &ldquo;Message deleted&rdquo;).</li>
        </ul>
        <p><strong>Anonymized (no longer linked to you):</strong></p>
        <ul>
          <li>
            text you posted in group chat, discussion comments and submitted solutions, poll votes, referral records and
            reports you filed. They appear as &ldquo;Deleted User&rdquo; and your name, username, email and phone number are
            removed from your account.
          </li>
        </ul>
        <p>
          Deletion is permanent and cannot be undone. Your username, email and phone number can be used to register again.
          Full details are in the <Link to={`${LEGAL.paths.privacy}#account-deletion`}>Privacy Policy</Link>.
        </p>
      </>
    ),
  },
  {
    id: "cannot-sign-in",
    title: "Can't sign in? Request deletion by email",
    content: (
      <>
        <p>
          If you can&apos;t sign in &mdash; for example you no longer have access to your password or phone &mdash; you can ask us
          to delete your account. <strong>This route is handled manually by the SCORAM team</strong>, so it is not instant.
        </p>
        <ol>
          <li>
            Email <a href={`mailto:${LEGAL.contactEmail}?subject=SCORAM%20account%20deletion%20request`}>{LEGAL.contactEmail}</a> from
            the email address registered on the account, with the subject &ldquo;SCORAM account deletion request&rdquo;.
          </li>
          <li>Include your SCORAM username and the phone number registered on the account.</li>
          <li>We may ask you to confirm you own the account before deleting it, to protect it from someone else asking.</li>
          <li>Once confirmed, we delete the account the same way as described above and let you know when it&apos;s done.</li>
        </ol>
        <p>
          Developer: <Fill>{LEGAL.developerName}</Fill>
        </p>
      </>
    ),
  },
];

export default function DeleteAccount() {
  return (
    <LegalPage
      title="Delete your SCORAM account"
      subtitle="SCORAM – Learn | Discuss | Score"
      seoTitle="Delete Your SCORAM Account"
      description="How to delete your SCORAM account and associated data, and what happens to your information."
      path={LEGAL.paths.deleteAccount}
      sections={sections}
    />
  );
}
