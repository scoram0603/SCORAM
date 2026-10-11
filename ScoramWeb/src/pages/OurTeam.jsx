import { useState } from "react";
import { Link } from "react-router-dom";
import { ArrowRight } from "lucide-react";
import LandingNavbar from "../components/landing/LandingNavbar";
import LandingFooter from "../components/landing/LandingFooter";
import Seo from "../components/seo/Seo";

// OUR TEAM -- public page at /our-team, linked from the landing navbar (navLinks in
// data/landingContent.js). Narrative: purpose -> story -> founders -> shared vision -> CTA.
//
// All copy lives in the constants below so it can be edited without touching the layout.
// Nothing here states a date, user count, result or credential -- keep it that way unless the
// information is verified. The three "principles" are product principles, not feature claims.
//
// Founder photos are the real photographs supplied by the project owner (web-sized copies),
// served from /public/assets/team/. Replace a file at the same path to change a photo.
// Styles are Tailwind utilities plus one small <style> block (`ot-` prefix) for the hero motion,
// so nothing leaks into global CSS.

const HERO = {
  eyebrow: "The people behind SCORAM",
  titleA: "Built with a Purpose.",
  titleB: "Driven by Aspirants.",
  description:
    "Exam preparation is more than solving questions. It is about understanding your progress, learning from every attempt, and becoming better every day. SCORAM is being built to help aspirants prepare with greater clarity, confidence, and direction.",
};

const STORY = {
  heading: "Why We Started SCORAM",
  paragraphs: [
    "Every aspirant puts time, effort, and hope into preparing for an exam. But preparation becomes more meaningful when students can practise consistently, understand their performance, identify areas for improvement, and track their progress.",
    "SCORAM is built around this belief. Our goal is to bring useful preparation resources and meaningful performance insights together in one place, helping aspirants make every practice session count.",
    "We are continuing to build and improve SCORAM with students' real preparation needs in mind.",
  ],
  // The four things the story says a student should be able to do, in order.
  steps: ["Practise consistently", "Understand your performance", "Identify what to improve", "Track your progress"],
};

const FOUNDERS_INTRO = {
  heading: "The People Building SCORAM",
  text: "Different strengths, one shared mission: helping aspirants prepare smarter.",
};

// `lead` + `body` together are the profile text, split only so the opening line can be set larger.
const FOUNDERS = [
  {
    name: "Sarvoch Priyadarshi",
    role: "Founder",
    photo: "/assets/team/sarvoch.jpg",
    alt: "Sarvoch, Founder of SCORAM",
    lead: "Every meaningful idea begins with a vision.",
    body: "Sarvoch envisioned SCORAM as a platform that could make exam preparation more focused, structured, and meaningful for aspirants. His vision is to help students move beyond simply practising questions and towards understanding their progress, recognising their strengths, and identifying where they can improve.",
  },
  {
    name: "Durgesh Maurya",
    role: "Co-founder",
    photo: "/assets/team/durgesh.jpg",
    alt: "Durgesh, Co-founder of SCORAM",
    lead: "Great ideas become meaningful when they are brought to life.",
    body: "As Co-founder of SCORAM, Durgesh contributes to turning the platform's vision into a practical digital experience. With a focus on technology, usability, and continuous improvement, he aims to help build a platform that makes exam preparation simpler, more accessible, and more effective for students.",
  },
];

const VISION = {
  heading: "More Than Practice. Progress with Purpose.",
  description:
    "We want SCORAM to help aspirants move beyond simply attempting questions. By bringing practice resources and performance insights together, we aim to help students understand their preparation and make more informed decisions about what to improve next.",
  principles: [
    { title: "Practise with purpose", text: "Make practice a consistent and meaningful part of exam preparation." },
    { title: "Understand your progress", text: "Help aspirants review their performance and recognise areas that need attention." },
    { title: "Keep moving forward", text: "Encourage continuous learning through informed practice and improvement." },
  ],
};

const CLOSING = {
  heading: "Every Aspirant Has a Goal. Let's Work Towards It.",
  text: "SCORAM is being built to support the journey — one practice session, one insight, and one step forward at a time.",
  cta: "Explore SCORAM",
  // "/" is the landing page for visitors and the student home for signed-in users.
  to: "/",
};

// Hero motion: the score line draws once on load, then the attempt dots appear. Under
// prefers-reduced-motion the finished drawing is shown immediately (no animation at all).
const SCOPED_CSS = `
.ot-line { stroke-dasharray: 1; stroke-dashoffset: 0; }
.ot-dot { opacity: 1; }
@media (prefers-reduced-motion: no-preference) {
  .ot-line { stroke-dashoffset: 1; animation: ot-draw 1.9s cubic-bezier(.4,0,.2,1) .3s forwards; }
  .ot-dot { opacity: 0; animation: ot-pop .5s ease-out forwards; }
}
@keyframes ot-draw { to { stroke-dashoffset: 0; } }
@keyframes ot-pop { to { opacity: 1; } }
`;

// Faint dotted grid -- graph paper, a nod to tracking progress. Pure CSS, no asset.
const GRID_STYLE = {
  backgroundImage: "radial-gradient(rgba(255,255,255,0.14) 1px, transparent 1px)",
  backgroundSize: "28px 28px",
};

// Points of the rising "score line": a few attempts, each one a little better than the last,
// with a dip along the way because progress is not a straight line.
const LINE_POINTS = [
  [28, 262],
  [130, 212],
  [215, 226],
  [310, 150],
  [390, 162],
  [512, 62],
];

function HeroVisual() {
  const d = LINE_POINTS.map(([x, y], i) => `${i === 0 ? "M" : "L"}${x} ${y}`).join(" ");
  const [lastX, lastY] = LINE_POINTS[LINE_POINTS.length - 1];
  return (
    <svg viewBox="0 0 560 300" className="h-auto w-full" role="img" aria-label="A line rising across several attempts toward a goal">
      <title>A line rising across several attempts toward a goal</title>
      {/* baseline */}
      <line x1="16" y1="284" x2="544" y2="284" stroke="rgba(255,255,255,0.28)" strokeWidth="1.5" />
      {/* soft area under the line */}
      <path d={`${d} L${lastX} 284 L${LINE_POINTS[0][0]} 284 Z`} fill="rgba(255,255,255,0.06)" />
      <path
        d={d}
        pathLength="1"
        className="ot-line"
        fill="none"
        stroke="#CFE1FA"
        strokeWidth="3.5"
        strokeLinejoin="round"
        strokeLinecap="butt"
      />
      {LINE_POINTS.slice(0, -1).map(([x, y], i) => (
        <circle
          key={`${x}-${y}`}
          cx={x}
          cy={y}
          r="6"
          fill="#0B2D5C"
          stroke="#CFE1FA"
          strokeWidth="3"
          className="ot-dot"
          style={{ animationDelay: `${0.5 + i * 0.3}s` }}
        />
      ))}
      {/* the goal */}
      <circle cx={lastX} cy={lastY} r="16" fill="rgba(255,107,0,0.22)" className="ot-dot" style={{ animationDelay: "2.2s" }} />
      <circle cx={lastX} cy={lastY} r="8" fill="#FF6B00" className="ot-dot" style={{ animationDelay: "2.2s" }} />
      <text x={lastX} y={lastY - 28} textAnchor="end" fill="#FFFFFF" fontSize="19" fontWeight="600" className="ot-dot" style={{ animationDelay: "2.3s" }}>
        Better every day
      </text>
    </svg>
  );
}

// Staircase for the story section: first step bottom-left, last step top-right. The list is in
// reading order in the DOM; flex-col-reverse only flips it visually so it climbs.
function StoryVisual({ steps }) {
  return (
    <div className="rounded-xl2 border border-primary-100 bg-primary-50/70 p-6 sm:p-8">
      <ol className="flex flex-col-reverse gap-3" aria-label="How preparation becomes more meaningful">
        {steps.map((label, i) => {
          const last = i === steps.length - 1;
          return (
            <li
              key={label}
              className="flex items-center gap-3 rounded-xl bg-white px-4 py-3.5 shadow-sm ring-1 ring-primary-100"
              style={{ marginLeft: `${i * 9}%` }}
            >
              <span
                aria-hidden="true"
                className={`h-3 w-3 shrink-0 rounded-full ${last ? "bg-accent-500 ring-4 ring-accent-100" : "bg-primary-500"}`}
              />
              <span className="text-[15px] font-semibold text-primary-600">{label}</span>
            </li>
          );
        })}
      </ol>
    </div>
  );
}

// Photo with a fixed 4:5 portrait frame. If the file is missing it falls back to an initial on
// brand navy instead of a broken-image icon.
function FounderPhoto({ founder }) {
  const [failed, setFailed] = useState(false);
  return (
    <div className="relative aspect-[4/5] w-full overflow-hidden rounded-xl2 bg-primary-50 ring-1 ring-primary-100">
      {failed ? (
        <div
          role="img"
          aria-label={founder.alt}
          className="flex h-full w-full items-center justify-center bg-primary-500 text-8xl font-bold text-white"
        >
          {founder.name.charAt(0)}
        </div>
      ) : (
        <img
          src={founder.photo}
          alt={founder.alt}
          loading="lazy"
          decoding="async"
          onError={() => setFailed(true)}
          className="h-full w-full object-cover object-[center_18%]"
        />
      )}
    </div>
  );
}

function FounderText({ founder }) {
  const headingId = `founder-${founder.name.toLowerCase()}`;
  return (
    <div className="max-w-xl">
      <p className="inline-flex items-center gap-2 rounded-full border border-primary-100 bg-white px-3.5 py-1.5 text-sm font-semibold text-primary-600">
        <span aria-hidden="true" className="h-2 w-2 rounded-full bg-accent-500" />
        {founder.role}
      </p>
      <h3 id={headingId} className="mt-4 text-4xl font-extrabold tracking-tight text-primary-600 sm:text-5xl">
        {founder.name}
      </h3>
      <p className="mt-6 text-xl font-semibold leading-snug text-ink-900 sm:text-2xl">{founder.lead}</p>
      <p className="mt-4 text-base leading-8 text-ink-600 sm:text-[17px] sm:leading-8">{founder.body}</p>
    </div>
  );
}

// One full-width editorial band per founder. The DOM order is what makes the mobile reading
// order right: Sarvoch = text then photo, Durgesh = photo then text. On desktop the two columns
// simply follow DOM order (Sarvoch: text left / photo right; Durgesh: photo left / text right).
function FounderBand({ founder, photoFirst, className, frameClassName, tightTop = false }) {
  const headingId = `founder-${founder.name.toLowerCase()}`;
  const photo = (
    <div className={`relative w-full max-w-md ${photoFirst ? "pb-4 pl-4 lg:justify-self-start" : "pb-4 pr-4 lg:justify-self-end"}`}>
      {/* offset backing block -- the one decorative touch on the photo */}
      <div
        aria-hidden="true"
        className={`absolute bottom-0 top-4 rounded-xl2 ${frameClassName} ${photoFirst ? "left-0 right-4" : "left-4 right-0"}`}
      />
      <div className="relative">
        <FounderPhoto founder={founder} />
      </div>
    </div>
  );
  const text = <FounderText founder={founder} />;

  return (
    <article aria-labelledby={headingId} className={className}>
      <div className={`mx-auto grid max-w-6xl items-center gap-10 px-4 pb-14 sm:px-6 lg:grid-cols-2 lg:gap-20 lg:px-8 lg:pb-20 ${tightTop ? "pt-8 lg:pt-10" : "pt-14 lg:pt-20"}`}>
        {photoFirst ? (
          <>
            {photo}
            {text}
          </>
        ) : (
          <>
            {text}
            {photo}
          </>
        )}
      </div>
    </article>
  );
}

export default function OurTeam() {
  return (
    <div className="min-h-screen overflow-x-hidden bg-white">
      <Seo
        title="Our Team"
        description="Meet the people behind SCORAM — built with a purpose to help aspirants prepare with greater clarity, confidence, and direction."
        path="/our-team"
      />
      <style>{SCOPED_CSS}</style>
      <LandingNavbar />

      <main>
        {/* 1 -- Hero */}
        <section className="relative overflow-hidden bg-primary-600" aria-labelledby="ot-hero-title">
          <div aria-hidden="true" className="pointer-events-none absolute inset-0" style={GRID_STYLE} />
          <div className="relative mx-auto max-w-6xl px-4 pb-16 pt-14 sm:px-6 sm:pb-20 sm:pt-16 lg:px-8 lg:pb-24 lg:pt-24">
            <p className="flex items-center gap-3 text-xs font-semibold uppercase tracking-[0.18em] text-secondary-100">
              <span aria-hidden="true" className="h-0.5 w-8 bg-accent-500" />
              {HERO.eyebrow}
            </p>
            <h1
              id="ot-hero-title"
              className="mt-6 text-[2.5rem] font-extrabold leading-[1.06] tracking-tight text-white sm:text-6xl lg:text-[4.5rem]"
            >
              {HERO.titleA}
              <span className="block text-secondary-100">{HERO.titleB}</span>
            </h1>
            <div className="mt-10 grid items-end gap-10 lg:mt-14 lg:grid-cols-12 lg:gap-12">
              <p className="max-w-xl text-base leading-8 text-white/85 sm:text-lg sm:leading-8 lg:col-span-5">{HERO.description}</p>
              <div className="w-full max-w-lg lg:col-span-7 lg:max-w-none">
                <HeroVisual />
              </div>
            </div>
          </div>
        </section>

        {/* 2 -- Story */}
        <section className="bg-white" aria-labelledby="ot-story-title">
          <div className="mx-auto grid max-w-6xl gap-12 px-4 py-16 sm:px-6 lg:grid-cols-12 lg:gap-16 lg:px-8 lg:py-28">
            <div className="lg:col-span-7">
              <h2 id="ot-story-title" className="text-3xl font-extrabold tracking-tight text-primary-600 sm:text-4xl lg:text-[2.6rem] lg:leading-tight">
                {STORY.heading}
              </h2>
              <div className="mt-8 max-w-2xl space-y-6 text-base leading-8 text-ink-600 sm:text-lg sm:leading-8">
                <p className="text-xl font-medium leading-9 text-ink-900 sm:text-[1.35rem]">{STORY.paragraphs[0]}</p>
                {STORY.paragraphs.slice(1).map((p) => (
                  <p key={p}>{p}</p>
                ))}
              </div>
            </div>
            <div className="lg:col-span-5 lg:pt-4">
              <StoryVisual steps={STORY.steps} />
            </div>
          </div>
        </section>

        {/* 3 -- Founders */}
        <section aria-labelledby="ot-founders-title">
          <div className="bg-surface">
            <div className="mx-auto max-w-6xl px-4 pb-2 pt-16 sm:px-6 lg:px-8 lg:pt-24">
              <h2 id="ot-founders-title" className="text-3xl font-extrabold tracking-tight text-primary-600 sm:text-4xl lg:text-[2.6rem] lg:leading-tight">
                {FOUNDERS_INTRO.heading}
              </h2>
              <p className="mt-4 max-w-2xl text-base leading-8 text-ink-600 sm:text-lg">{FOUNDERS_INTRO.text}</p>
            </div>
          </div>
          <FounderBand founder={FOUNDERS[0]} photoFirst={false} tightTop className="bg-surface" frameClassName="bg-primary-100" />
          <FounderBand
            founder={FOUNDERS[1]}
            photoFirst
            className="border-t border-primary-100 bg-white"
            frameClassName="bg-secondary-100"
          />
        </section>

        {/* 4 -- Shared vision */}
        <section className="bg-secondary-50" aria-labelledby="ot-vision-title">
          <div className="mx-auto max-w-6xl px-4 py-16 sm:px-6 lg:px-8 lg:py-28">
            <h2 id="ot-vision-title" className="max-w-3xl text-3xl font-extrabold tracking-tight text-primary-600 sm:text-4xl lg:text-[2.6rem] lg:leading-tight">
              {VISION.heading}
            </h2>
            <p className="mt-5 max-w-3xl text-base leading-8 text-ink-600 sm:text-lg">{VISION.description}</p>

            <ol className="mt-14 grid gap-12 md:grid-cols-3 md:gap-0">
              {VISION.principles.map((item, i) => {
                const last = i === VISION.principles.length - 1;
                return (
                  <li key={item.title} className="relative border-t-2 border-primary-500/25 pt-8 md:pr-10">
                    {/* node on the progress line; the last one is the goal */}
                    <span
                      aria-hidden="true"
                      className={`absolute -top-[7px] left-0 h-3 w-3 rounded-full ring-4 ring-secondary-50 ${last ? "bg-accent-500" : "bg-primary-500"}`}
                    />
                    <p className="text-5xl font-extrabold leading-none tracking-tight text-secondary-500">{`0${i + 1}`}</p>
                    <h3 className="mt-5 text-base font-bold uppercase tracking-wide text-primary-600">{item.title}</h3>
                    <p className="mt-3 max-w-xs text-base leading-7 text-ink-600">{item.text}</p>
                  </li>
                );
              })}
            </ol>
          </div>
        </section>

        {/* 5 -- Closing statement */}
        <section className="relative overflow-hidden bg-primary-600" aria-labelledby="ot-closing-title">
          <div aria-hidden="true" className="pointer-events-none absolute inset-0" style={GRID_STYLE} />
          <div className="relative mx-auto max-w-4xl px-4 py-20 text-center sm:px-6 lg:px-8 lg:py-28">
            <h2 id="ot-closing-title" className="text-3xl font-extrabold leading-tight tracking-tight text-white sm:text-5xl sm:leading-[1.1]">
              {CLOSING.heading}
            </h2>
            <p className="mx-auto mt-6 max-w-2xl text-base leading-8 text-white/85 sm:text-lg">{CLOSING.text}</p>
            <Link
              to={CLOSING.to}
              className="mt-10 inline-flex items-center justify-center gap-2 rounded-xl bg-accent-500 px-7 py-4 text-base font-bold text-primary-900 shadow-floating transition-colors hover:bg-accent-100 focus-visible:outline-white"
            >
              {CLOSING.cta}
              <ArrowRight className="h-4 w-4" strokeWidth={2.75} />
            </Link>
          </div>
        </section>
      </main>

      <LandingFooter />
    </div>
  );
}
