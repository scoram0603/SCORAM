import LandingNavbar from "../components/landing/LandingNavbar";
import LandingFooter from "../components/landing/LandingFooter";
import Seo from "../components/seo/Seo";

export default function LegalPage({ title, path, updated, children }) {
  return (
    <div className="min-h-screen overflow-x-hidden bg-white">
      <Seo title={title} path={path} description={`${title} for SCORAM.`} />
      <LandingNavbar />
      <main className="mx-auto max-w-3xl px-4 py-16 sm:px-6 lg:px-8">
        <h1 className="text-3xl font-extrabold text-ink-900">{title}</h1>
        <p className="mt-2 text-sm text-ink-400">Last updated: {updated}</p>
        <div className="mt-8 space-y-5 text-[15px] leading-relaxed text-ink-600 [&_h2]:mt-8 [&_h2]:text-lg [&_h2]:font-bold [&_h2]:text-ink-900 [&_ul]:list-disc [&_ul]:space-y-1.5 [&_ul]:pl-5">
          {children}
        </div>
      </main>
      <LandingFooter />
    </div>
  );
}
