// SINGLE SOURCE OF TRUTH for facts shown on the legal pages (Privacy Policy, Terms & Conditions,
// Delete Account). Any value that starts with "[" is an UNRESOLVED PLACEHOLDER -- LegalPage.jsx
// renders those highlighted in amber so they cannot be missed, and LEGAL_IMPLEMENTATION_NOTES.md lists every
// one. Resolve them here (one place) before publishing.
import { seoConfig } from "./seo";

export const LEGAL = {
  appName: "SCORAM",
  tagline: "Learn | Discuss | Score",

  // Dates: bump `lastUpdated` whenever the policy text changes in substance. Effective date is the
  // day this version is first published -- CONFIRM it matches the actual publication date.
  effectiveDate: "October 6, 2026",
  lastUpdated: "October 6, 2026",

  // MUST match the developer name shown on the Google Play listing exactly.
  developerName: "Durgesh Kumar",

  // Official privacy/support mailbox. info@scoram.in is what the site already publishes
  // (seoConfig.contact.email) -- confirm it is the intended privacy contact.
  contactEmail: seoConfig.contact.email,

  // Deliberately NOT inferred from where the developer is based -- needs legal review.
  governingLaw: "These Terms are governed by and construed in accordance with the laws of India. The courts located in Budaun, Uttar Pradesh, India shall have exclusive jurisdiction over any disputes arising out of or in connection with these Terms.",

  paths: {
    privacy: "/privacy-policy",
    terms: "/terms-and-conditions",
    deleteAccount: "/delete-account",
  },
};

export const isPlaceholder = (value) => typeof value === "string" && value.trim().startsWith("[");
