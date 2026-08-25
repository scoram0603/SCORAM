// LANDING PAGE CONTENT -- all editable marketing copy lives here, separate from the section
// components in src/components/landing/. Edit this file to change wording; edit the components
// to change layout/behavior.
//
// IMPORTANT: testimonials below are placeholder copy, clearly marked as such -- do NOT mark them
// as "verified reviews" anywhere (no star ratings, no "verified" badge) until real student
// testimonials are collected. See LANDING_REPORT.md for the full list of things to swap in.

import {
  FileClock,
  Layers,
  Timer,
  ClipboardCheck,
  Zap,
  Users,
  UserPlus,
  Target,
  TrendingUp,
  Trophy,
} from "lucide-react";

export const navLinks = [
  { label: "Home", href: "#top" },
  { label: "PYP Practice", to: "/pyq" },
  { label: "Question Bank", to: "/question-bank" },
  { label: "Tests", to: "/tests" },
  { label: "Mock Tests", to: "/tests/mock" },
  { label: "Quizzes", to: "/quizzes" },
  { label: "Community", to: "/chat" },
  { label: "Pricing", href: "#faq" },
];

export const footerLinks = {
  quick: [
    { label: "Home", href: "#top" },
    { label: "PYP Practice", to: "/pyq" },
    { label: "Question Bank", to: "/question-bank" },
    { label: "Tests", to: "/tests" },
    { label: "Mock Tests", to: "/tests/mock" },
    { label: "Quizzes", to: "/quizzes" },
  ],
  community: [
    { label: "Group Chat", to: "/chat" },
    { label: "Discussions", to: "/discussions" },
    { label: "Leaderboard", to: "/leaderboard" },
  ],
  support: [
    { label: "Help Center", href: "#faq" },
    { label: "Contact Us", href: "mailto:info@scoram.in" },
    { label: "Privacy Policy", to: "/privacy-policy" },
    { label: "Terms & Conditions", to: "/terms" },
  ],
};

// Support/legal pages (Privacy Policy, Terms) don't exist in the app yet -- linked above for
// structure/SEO completeness, but see LANDING_REPORT.md: routes need to be created before these
// stop 404ing.

export const features = [
  {
    icon: FileClock,
    title: "PYP Practice",
    description: "Practice real previous-year papers under real exam conditions.",
  },
  {
    icon: Layers,
    title: "Question Bank",
    description: "Practice topic-wise questions with detailed, multi-method solutions.",
  },
  {
    icon: Timer,
    title: "Tests",
    description: "Timed exam-wise practice that mirrors the real test experience.",
  },
  {
    icon: ClipboardCheck,
    title: "Mock Tests",
    description: "Full-length mock tests that simulate the actual exam environment.",
  },
  {
    icon: Zap,
    title: "Quizzes",
    description: "Quick topic-wise challenges to sharpen weak areas and build streaks.",
  },
  {
    icon: Users,
    title: "Community",
    description: "Discuss questions, ask doubts, and learn together with other students.",
  },
];

export const howItWorks = [
  { step: "01", icon: UserPlus, title: "Create Account", description: "Sign up for free and set up your profile in under a minute." },
  { step: "02", icon: Target, title: "Choose Your Exam", description: "Pick the exams you're preparing for from SCORAM's exam list." },
  { step: "03", icon: TrendingUp, title: "Practice & Learn", description: "Solve questions, attempt tests, and discuss with the community." },
  { step: "04", icon: Trophy, title: "Track Progress & Score Higher", description: "Watch your accuracy and streaks improve as you keep practicing." },
];

// PLACEHOLDER -- clearly editable, clearly not real reviews. Swap these for real student
// testimonials (with permission) before launch, and never attach star ratings or a "verified"
// badge to placeholder copy. See LANDING_REPORT.md.
export const testimonials = [
  {
    quote: "SCORAM helped me practice previous year questions in a much more structured way than jumping between PDFs.",
    name: "Placeholder Student",
    exam: "SSC CGL Aspirant",
  },
  {
    quote: "Being able to see multiple ways to solve the same question made shortcuts finally click for me.",
    name: "Placeholder Student",
    exam: "Railway Exam Aspirant",
  },
  {
    quote: "The discussion threads on tough questions are genuinely useful — better than solving alone.",
    name: "Placeholder Student",
    exam: "Banking Exam Aspirant",
  },
];

export const faqs = [
  {
    question: "What is SCORAM?",
    answer: "SCORAM is an all-in-one platform for competitive and government exam preparation — previous year papers, a topic-wise question bank, practice and mock tests, quizzes, and a student discussion community, all in one place.",
  },
  {
    question: "Which exams are supported?",
    answer: "SCORAM covers a growing list of competitive and government exams. You can browse the full, current list on the Popular Exams section above or in Question Bank.",
  },
  {
    question: "What is PYP Practice?",
    answer: "PYP Practice lets you attempt full previous-year papers as a single timed test, so you experience the real exam pattern instead of solving questions one at a time.",
  },
  {
    question: "What is the Question Bank?",
    answer: "Question Bank is SCORAM's topic-wise question library — filter by subject, topic, exam, or year, and see multiple solving methods and discussions on each question.",
  },
  {
    question: "Can I discuss questions with other students?",
    answer: "Yes. Every question supports discussion, and SCORAM also has group chat rooms and direct messages so you can learn alongside other students, not alone.",
  },
  {
    question: "Is SCORAM available on mobile?",
    answer: "SCORAM's web app is fully responsive and works on any mobile browser. A dedicated mobile app is in progress — see the app section above.",
  },
  {
    question: "How do I create an account?",
    answer: "Tap \"Sign Up\", enter your details and a username, and you're ready to start practicing — it takes less than a minute.",
  },
  {
    question: "Is SCORAM free to use?",
    answer: "Yes — you can create a free account and start practicing right away.",
  },
];
