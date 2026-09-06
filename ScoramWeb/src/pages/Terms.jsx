import LegalPage from "./LegalPage";

// Drafted to cover what SCORAM actually does and collects (see PrivacyPolicy.jsx for the data
// side) -- acceptable use, content ownership, liability, termination, and the right to update
// these terms. Still a draft: have it reviewed by a lawyer before treating it as binding.
export default function Terms() {
  return (
    <LegalPage title="Terms of Service" path="/terms" updated="September 6, 2026">
      <p>
        These Terms of Service ("Terms") govern your access to and use of SCORAM — the website,
        and any related apps we offer (together, the "Service"). By creating an account or
        otherwise using the Service, you agree to these Terms. If you don't agree, please don't
        use the Service.
      </p>

      <h2>1. Your Account</h2>
      <ul>
        <li>You must provide accurate information when registering (name, username, email, phone number) and keep it up to date.</li>
        <li>You're responsible for everything that happens under your account, including keeping your password confidential.</li>
        <li>You must be old enough to legally use an online learning platform in your country; if you're a minor, you should have a parent or guardian's permission.</li>
      </ul>

      <h2>2. What You Can Do on SCORAM</h2>
      <p>SCORAM is built for exam preparation. You're welcome to:</p>
      <ul>
        <li>Attempt Previous Year Papers, Mock Tests, Practice Tests, and Quizzes</li>
        <li>Browse and bookmark questions in the Question Bank</li>
        <li>Track your own progress, XP, streaks, and badges</li>
        <li>Participate in Group Chat and Discussions, post comments, and share solutions</li>
        <li>Refer other students using your referral link</li>
      </ul>

      <h2>3. What You Cannot Do</h2>
      <p>To keep SCORAM fair and usable for everyone, you agree not to:</p>
      <ul>
        <li>Cheat or help others cheat during timed attempts (Mock Tests, Practice Tests, Quizzes, PYP attempts) — for example, sharing answers while a test is live, or using multiple accounts to gain extra attempts</li>
        <li>Post content in Chat, Discussions, comments, or solutions that is abusive, harassing, hateful, sexually explicit, or otherwise unlawful</li>
        <li>Impersonate another person, or misrepresent your affiliation with anyone</li>
        <li>Upload content you don't have the rights to, or that infringes someone else's copyright</li>
        <li>Scrape, reverse-engineer, or use automated tools against the Service, or attempt to access accounts or data that aren't yours</li>
        <li>Resell, redistribute, or commercially exploit SCORAM's questions, papers, or other content without our permission</li>
        <li>Interfere with the Service's normal operation (e.g. attempting to disrupt servers, tests, or other students' access)</li>
      </ul>

      <h2>4. Content You Post</h2>
      <p>
        You keep ownership of the comments, chat messages, solutions, and other content you post
        on SCORAM. By posting it, you give us a non-exclusive, worldwide license to host, display,
        and distribute it within the Service — for example, showing your solution to other
        students, or your message in a chat room you've joined.
      </p>
      <p>
        We may remove or hide content, and restrict who can see it, if we reasonably believe it
        violates these Terms or our community expectations, without needing your prior consent.
      </p>

      <h2>5. Questions, Papers &amp; Other SCORAM Content</h2>
      <p>
        Previous Year Papers, Question Bank items, Mock Tests, and other exam material on SCORAM
        are provided to help you prepare and are owned by SCORAM or its licensors. You may use
        them for your own personal exam preparation; you may not redistribute or republish them
        elsewhere without permission. We work to keep this content accurate and up to date but
        can't guarantee it is error-free or that it matches every detail of an actual exam.
      </p>

      <h2>6. Disclaimer &amp; Limitation of Liability</h2>
      <ul>
        <li>SCORAM is provided "as is." We don't guarantee the Service will be uninterrupted, error-free, or available at all times — practice attempts, chat, or any other feature may occasionally be unavailable due to maintenance, outages, or issues outside our control.</li>
        <li>SCORAM is a preparation tool, not a guarantee of any exam outcome. We're not responsible for how you perform in an actual competitive exam.</li>
        <li>To the maximum extent permitted by law, SCORAM and its team are not liable for any indirect, incidental, or consequential loss arising from your use of (or inability to use) the Service — including lost practice time, lost data, or lost opportunities — except where such liability cannot be excluded by law.</li>
        <li>Nothing in these Terms limits our liability for something the law doesn't allow us to limit (such as liability arising from fraud).</li>
      </ul>

      <h2>7. Suspending or Terminating Accounts</h2>
      <p>
        We may suspend or terminate your account, with or without prior notice, if we reasonably
        believe you've violated these Terms — including cheating, abusive behavior in Chat or
        Discussions, or attempting to disrupt the Service for other students. You may also delete
        your own account at any time by contacting us (see Section 9).
      </p>

      <h2>8. Changes to These Terms</h2>
      <p>
        We may update these Terms from time to time as SCORAM's features change. If we make a
        material change, we'll update the "Last updated" date above. Continuing to use SCORAM
        after a change means you accept the updated Terms.
      </p>

      <h2>9. Contact</h2>
      <p>Questions about these Terms can be sent to info@scoram.in.</p>

      <p className="text-sm text-ink-400">
        This is a working draft that reflects how SCORAM currently operates. We'd recommend having
        it reviewed by a qualified lawyer before relying on it as your final, binding Terms of
        Service.
      </p>
    </LegalPage>
  );
}
