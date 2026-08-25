import { useEffect, useState } from "react";
import Seo from "../components/seo/Seo";
import { seoConfig } from "../config/seo";
import { getPublicStats } from "../api/publicStats";
import { faqs } from "../data/landingContent";

import LandingNavbar from "../components/landing/LandingNavbar";
import HeroSection from "../components/landing/HeroSection";
import StatsSection from "../components/landing/StatsSection";
import FeaturesSection from "../components/landing/FeaturesSection";
import PYPSection from "../components/landing/PYPSection";
import QuestionBankSection from "../components/landing/QuestionBankSection";
import SolutionsSection from "../components/landing/SolutionsSection";
import CommunitySection from "../components/landing/CommunitySection";
import HowItWorksSection from "../components/landing/HowItWorksSection";
import PopularExamsSection from "../components/landing/PopularExamsSection";
import TestimonialsSection from "../components/landing/TestimonialsSection";
import AppPromotionSection from "../components/landing/AppPromotionSection";
import FAQSection from "../components/landing/FAQSection";
import FinalCTA from "../components/landing/FinalCTA";
import LandingFooter from "../components/landing/LandingFooter";

function buildJsonLd() {
  return {
    "@context": "https://schema.org",
    "@graph": [
      {
        "@type": "EducationalOrganization",
        "@id": `${seoConfig.siteUrl}/#organization`,
        name: seoConfig.siteName,
        url: seoConfig.siteUrl,
        logo: `${seoConfig.siteUrl}${seoConfig.logo}`,
        description: seoConfig.defaultDescription,
        email: seoConfig.contact.email,
        telephone: seoConfig.contact.phone,
      },
      {
        "@type": "WebSite",
        "@id": `${seoConfig.siteUrl}/#website`,
        url: seoConfig.siteUrl,
        name: seoConfig.siteName,
        publisher: { "@id": `${seoConfig.siteUrl}/#organization` },
      },
      {
        "@type": "FAQPage",
        mainEntity: faqs.map((item) => ({
          "@type": "Question",
          name: item.question,
          acceptedAnswer: { "@type": "Answer", text: item.answer },
        })),
      },
    ],
  };
}

export default function Landing() {
  const [stats, setStats] = useState(null);

  useEffect(() => {
    const controller = new AbortController();
    getPublicStats({ signal: controller.signal })
      .then(setStats)
      .catch(() => {}); // honest fallback: sections just show "—" instead of a fake number
    return () => controller.abort();
  }, []);

  return (
    <div className="min-h-screen overflow-x-hidden bg-white">
      <Seo
        title={undefined}
        description={seoConfig.defaultDescription}
        path="/"
        jsonLd={buildJsonLd()}
      />

      <LandingNavbar />

      <main>
        {/* Single H1 lives inside HeroSection */}
        <HeroSection stats={stats} />
        <StatsSection stats={stats} />
        <FeaturesSection />
        <PYPSection />
        <QuestionBankSection />
        <SolutionsSection />
        <CommunitySection />
        <HowItWorksSection />
        <PopularExamsSection />
        <TestimonialsSection />
        <AppPromotionSection />
        <FinalCTA />
        <FAQSection />
      </main>

      <LandingFooter />
    </div>
  );
}
