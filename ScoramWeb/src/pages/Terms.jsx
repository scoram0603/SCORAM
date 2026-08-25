import LegalPage from "./LegalPage";

// DRAFT TEMPLATE -- generic placeholder structure, not reviewed by a lawyer. Replace the bracketed
// specifics and have this reviewed before it's relied on. See LANDING_REPORT.md.
export default function Terms() {
  return (
    <LegalPage title="Terms & Conditions" path="/terms" updated="[Add date]">
      <p>
        These Terms & Conditions govern your use of the SCORAM platform. By creating an account
        or using SCORAM, you agree to these terms.
      </p>

      <h2>Using SCORAM</h2>
      <ul>
        <li>You must provide accurate account information</li>
        <li>You're responsible for activity under your account</li>
        <li>Content you post (comments, chat messages) must follow our community guidelines</li>
      </ul>

      <h2>Content</h2>
      <p>
        Questions, papers, and solutions on SCORAM are provided for exam preparation purposes.
        We work to keep content accurate but cannot guarantee it is error-free.
      </p>

      <h2>Account Suspension</h2>
      <p>
        We may suspend or terminate accounts that violate these terms or our community
        guidelines, including in chat and discussions.
      </p>

      <h2>Changes</h2>
      <p>We may update these terms from time to time; continued use means you accept the changes.</p>

      <h2>Contact</h2>
      <p>Questions about these terms can be sent to info@scoram.in.</p>

      <p className="text-sm text-ink-400">
        [This is a draft placeholder — replace with terms reviewed by qualified legal counsel
        before launch.]
      </p>
    </LegalPage>
  );
}
