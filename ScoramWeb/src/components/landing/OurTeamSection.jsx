import { useState } from "react";
import { Link } from "react-router-dom";
import { ArrowRight } from "lucide-react";
import ScrollReveal from "./ScrollReveal";

// Landing teaser for the full /our-team page (pages/OurTeam.jsx). Photos are the same files that page uses,
// served from /public/assets/team/; if one fails to load the card falls back to the initial.
const MEMBERS = [
  {
    name: "Sarvoch Priyadarshi",
    role: "Founder",
    photo: "/assets/team/sarvoch.jpg",
    alt: "Sarvoch Priyadarshi, Founder of SCORAM",
    line: "Envisioned SCORAM as a platform that makes exam preparation more focused, structured, and meaningful.",
  },
  {
    name: "Durgesh Maurya",
    role: "Co-founder",
    photo: "/assets/team/durgesh.jpg",
    alt: "Durgesh Maurya, Co-founder of SCORAM",
    line: "Turns the vision into a practical digital experience, with a focus on technology, usability, and continuous improvement.",
  },
];

function MemberCard({ member }) {
  const [failed, setFailed] = useState(false);
  return (
    <div className="flex flex-col items-center rounded-2xl border border-primary-100 bg-white p-6 text-center shadow-card">
      <div className="h-28 w-28 overflow-hidden rounded-full bg-primary-50 ring-4 ring-primary-100">
        {failed ? (
          <div role="img" aria-label={member.alt} className="flex h-full w-full items-center justify-center bg-primary-500 text-4xl font-bold text-white">
            {member.name.charAt(0)}
          </div>
        ) : (
          <img
            src={member.photo}
            alt={member.alt}
            loading="lazy"
            decoding="async"
            onError={() => setFailed(true)}
            className="h-full w-full object-cover object-[center_18%]"
          />
        )}
      </div>
      <h3 className="mt-4 text-lg font-extrabold text-ink-900">{member.name}</h3>
      <p className="mt-0.5 text-sm font-semibold text-teal-500">{member.role}</p>
      <p className="mt-3 max-w-xs text-sm leading-relaxed text-ink-600">{member.line}</p>
    </div>
  );
}

export default function OurTeamSection() {
  return (
    <section id="our-team" className="bg-surface py-20 lg:py-28" aria-labelledby="landing-team-title">
      <div className="mx-auto max-w-5xl px-4 sm:px-6 lg:px-8">
        <ScrollReveal className="text-center">
          <p className="text-sm font-semibold uppercase tracking-wide text-teal-500">Our Team</p>
          <h2 id="landing-team-title" className="mt-2 text-3xl font-extrabold tracking-tight text-ink-900 sm:text-4xl">
            Built with a Purpose. Driven by Aspirants.
          </h2>
          <p className="mx-auto mt-4 max-w-2xl text-[15px] leading-relaxed text-ink-600">
            Different strengths, one shared mission: helping aspirants prepare smarter.
          </p>
        </ScrollReveal>

        <div className="mx-auto mt-10 grid max-w-3xl grid-cols-1 gap-6 sm:grid-cols-2">
          {MEMBERS.map((m, i) => (
            <ScrollReveal key={m.name} delay={i * 120}>
              <MemberCard member={m} />
            </ScrollReveal>
          ))}
        </div>

        <ScrollReveal className="mt-10 text-center">
          <Link
            to="/our-team"
            className="inline-flex items-center gap-2 rounded-xl2 bg-primary-600 px-6 py-3 text-sm font-semibold text-white transition-colors hover:bg-primary-700"
          >
            Meet the team
            <ArrowRight className="h-4 w-4" strokeWidth={2.5} />
          </Link>
        </ScrollReveal>
      </div>
    </section>
  );
}
