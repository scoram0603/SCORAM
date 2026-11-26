import { Link } from "react-router-dom";
import { Smartphone, Wifi, BellRing, ShieldCheck, ArrowRight, Download } from "lucide-react";
import LandingNavbar from "../components/landing/LandingNavbar";
import LandingFooter from "../components/landing/LandingFooter";
import RouteSeo from "../components/seo/RouteSeo";
import AssetImage from "../components/landing/AssetImage";
import { PLAY_STORE_URL } from "../config/seoRoutes";

const BENEFITS = [
  { icon: Smartphone, title: "Practise anywhere", text: "Previous year papers, the PYQ question bank and mock tests in your pocket, on phone or tablet." },
  { icon: Wifi, title: "Built for real-world networks", text: "Designed to stay smooth even on slower mobile connections." },
  { icon: BellRing, title: "Stay on track", text: "Get notified about streaks and new tests so your preparation stays consistent." },
  { icon: ShieldCheck, title: "One account everywhere", text: "Log in with the same SCORAM account on the app and on scoram.in — your progress stays in sync." },
];

const STEPS = [
  { title: "Open Google Play", text: "Tap the Download button below on your Android phone, or search for “SCORAM” in the Play Store." },
  { title: "Install the SCORAM app", text: "Tap Install and wait for the download to finish." },
  { title: "Register or Login", text: "Create a new account, or log in with your existing SCORAM account, and start practising." },
];

export default function DownloadApp() {
  return (
    <div className="min-h-screen overflow-x-hidden bg-white">
      <RouteSeo routeKey="downloadApp" />
      <LandingNavbar />

      <main>
        <section className="bg-gradient-to-b from-primary-50/60 via-white to-white">
          <div className="mx-auto grid max-w-6xl items-center gap-12 px-4 pb-16 pt-12 sm:px-6 lg:grid-cols-2 lg:px-8 lg:pb-24 lg:pt-20">
            <div>
              <p className="text-sm font-semibold uppercase tracking-wide text-teal-500">SCORAM App</p>
              <h1 className="mt-2 text-4xl font-extrabold tracking-tight text-ink-900 sm:text-5xl">
                Download the SCORAM App
              </h1>
              <p className="mt-4 max-w-lg text-base leading-relaxed text-ink-600">
                Prepare for SSC, Railway and other government exams anywhere. Practise previous year
                papers, the topic-wise PYQ question bank and mock tests with the SCORAM Android app.
              </p>
              <div className="mt-7 flex flex-wrap items-center gap-3">
                <a
                  href={PLAY_STORE_URL}
                  className="inline-flex items-center gap-2 rounded-xl bg-primary-600 px-6 py-3.5 text-[15px] font-semibold text-white shadow-floating transition-transform hover:-translate-y-0.5 hover:bg-primary-700"
                >
                  <Download className="h-4 w-4" strokeWidth={2.5} />
                  Download on Google Play
                </a>
                <Link
                  to="/register"
                  className="inline-flex items-center gap-2 rounded-xl border border-primary-100 bg-white px-6 py-3.5 text-[15px] font-semibold text-primary-600 transition-colors hover:bg-primary-50"
                >
                  Register on the Website
                  <ArrowRight className="h-4 w-4" strokeWidth={2.5} />
                </Link>
              </div>
              <p className="mt-4 text-sm text-ink-400">
                Prefer the browser? SCORAM works on any phone or computer at scoram.in —{" "}
                <Link to="/login" className="font-semibold text-secondary-500 hover:underline">
                  Login
                </Link>{" "}
                to continue.
              </p>
            </div>

            <div className="mx-auto w-full max-w-xs">
              <AssetImage
                src="/assets/landing/mobile-app-preview.png"
                slot="app-promo-preview"
                alt="SCORAM mobile app preview on a smartphone"
                label="mobile-app-preview.png"
                aspect="aspect-[9/16]"
                className="w-full rounded-[2rem] border-4 border-primary-900 object-cover shadow-cardHover"
              />
            </div>
          </div>
        </section>

        <section className="mx-auto max-w-6xl px-4 py-16 sm:px-6 lg:px-8">
          <h2 className="text-2xl font-extrabold text-ink-900 sm:text-3xl">Why use the SCORAM app</h2>
          <div className="mt-8 grid gap-5 sm:grid-cols-2">
            {BENEFITS.map((b) => (
              <div key={b.title} className="flex gap-4 rounded-2xl border border-primary-100 bg-white p-5 shadow-card">
                <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-teal-50 text-teal-500">
                  <b.icon className="h-5 w-5" strokeWidth={2.25} />
                </span>
                <div>
                  <h3 className="text-base font-bold text-ink-900">{b.title}</h3>
                  <p className="mt-1 text-sm leading-relaxed text-ink-600">{b.text}</p>
                </div>
              </div>
            ))}
          </div>
        </section>

        <section className="bg-surface">
          <div className="mx-auto max-w-6xl px-4 py-16 sm:px-6 lg:px-8">
            <h2 className="text-2xl font-extrabold text-ink-900 sm:text-3xl">How to get started</h2>
            <ol className="mt-8 grid gap-5 md:grid-cols-3">
              {STEPS.map((step, i) => (
                <li key={step.title} className="rounded-2xl bg-white p-5 shadow-card">
                  <span className="flex h-9 w-9 items-center justify-center rounded-full bg-primary-600 text-sm font-bold text-white">
                    {i + 1}
                  </span>
                  <h3 className="mt-3 text-base font-bold text-ink-900">{step.title}</h3>
                  <p className="mt-1 text-sm leading-relaxed text-ink-600">{step.text}</p>
                </li>
              ))}
            </ol>
          </div>
        </section>
      </main>

      <LandingFooter />
    </div>
  );
}
