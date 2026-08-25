import LegalPage from "./LegalPage";

// DRAFT TEMPLATE -- generic placeholder structure, not reviewed by a lawyer. Replace the bracketed
// specifics and have this reviewed before it's relied on. See LANDING_REPORT.md.
export default function PrivacyPolicy() {
  return (
    <LegalPage title="Privacy Policy" path="/privacy-policy" updated="[Add date]">
      <p>
        This Privacy Policy explains how SCORAM ("we", "our", "us") collects, uses, and protects
        information when you use the SCORAM platform.
      </p>

      <h2>Information We Collect</h2>
      <ul>
        <li>Account information you provide: name, username, email, phone number</li>
        <li>Content you create: answers, comments, chat messages, test attempts</li>
        <li>Usage data: pages visited, features used, device/browser information</li>
      </ul>

      <h2>How We Use Information</h2>
      <ul>
        <li>To provide and improve the SCORAM platform</li>
        <li>To personalize your practice experience and track your progress</li>
        <li>To communicate with you about your account and platform updates</li>
      </ul>

      <h2>Data Sharing</h2>
      <p>
        We do not sell your personal information. Information may be shared with service
        providers who help us operate the platform, or when required by law.
      </p>

      <h2>Your Choices</h2>
      <p>
        You can update your account information in Settings, and can request account deletion by
        contacting us at [support email].
      </p>

      <h2>Contact</h2>
      <p>Questions about this policy can be sent to info@scoram.in.</p>

      <p className="text-sm text-ink-400">
        [This is a draft placeholder — replace with a policy reviewed by qualified legal counsel
        before launch.]
      </p>
    </LegalPage>
  );
}
