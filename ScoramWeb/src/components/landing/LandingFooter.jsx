import { Link } from "react-router-dom";
import { Mail, Phone, MapPin } from "lucide-react";
import logo from "../../assets/scoram-logo-horizontal.png";
import { footerLinks } from "../../data/landingContent";
import { seoConfig } from "../../config/seo";

function FooterLink({ item }) {
  const className = "text-[13px] text-white/65 transition-colors hover:text-white";
  if (item.to) return <Link to={item.to} className={className}>{item.label}</Link>;
  return <a href={item.href} className={className}>{item.label}</a>;
}

function FooterColumn({ title, items }) {
  return (
    <div>
      <h4 className="text-sm font-bold text-white">{title}</h4>
      <ul className="mt-4 space-y-2.5">
        {items.map((item) => (
          <li key={item.label}>
            <FooterLink item={item} />
          </li>
        ))}
      </ul>
    </div>
  );
}

export default function LandingFooter() {
  return (
    <footer className="bg-primary-900 pt-16 text-white">
      <div className="mx-auto grid max-w-7xl grid-cols-2 gap-8 px-4 pb-12 sm:px-6 md:grid-cols-5 lg:px-8">
        <div className="col-span-2 md:col-span-2">
          <img src={logo} alt="SCORAM" className="h-9 w-auto brightness-0 invert" />
          <p className="mt-4 max-w-xs text-sm leading-relaxed text-white/60">
            SCORAM is an all-in-one learning platform to help you learn, discuss, practice and
            score higher in government exams.
          </p>
          {/* Social icons render only once real profile URLs are added to seoConfig.socialLinks --
              see LANDING_REPORT.md. No placeholder "#" links. */}
          {seoConfig.socialLinks.length > 0 && (
            <div className="mt-5 flex gap-3">
              {seoConfig.socialLinks.map((social) => (
                <a
                  key={social.label}
                  href={social.url}
                  target="_blank"
                  rel="noopener noreferrer"
                  aria-label={social.label}
                  className="flex h-9 w-9 items-center justify-center rounded-full bg-white/10 transition-colors hover:bg-white/20"
                >
                  <social.icon className="h-4 w-4" strokeWidth={2} />
                </a>
              ))}
            </div>
          )}
        </div>

        <FooterColumn title="Quick Links" items={footerLinks.quick} />
        <FooterColumn title="Community" items={footerLinks.community} />
        <FooterColumn title="Support" items={footerLinks.support} />

        <div>
          <h4 className="text-sm font-bold text-white">Contact Us</h4>
          <ul className="mt-4 space-y-3">
            <li className="flex items-start gap-2.5 text-[13px] text-white/65">
              <Mail className="mt-0.5 h-4 w-4 shrink-0" strokeWidth={2} />
              <a href={`mailto:${seoConfig.contact.email}`} className="hover:text-white">
                {seoConfig.contact.email}
              </a>
            </li>
            <li className="flex items-start gap-2.5 text-[13px] text-white/65">
              <Phone className="mt-0.5 h-4 w-4 shrink-0" strokeWidth={2} />
              <a href={`tel:${seoConfig.contact.phone.replace(/\s+/g, "")}`} className="hover:text-white">
                {seoConfig.contact.phone}
              </a>
            </li>
            <li className="flex items-start gap-2.5 text-[13px] text-white/65">
              <MapPin className="mt-0.5 h-4 w-4 shrink-0" strokeWidth={2} />
              India
            </li>
          </ul>
        </div>
      </div>

      <div className="border-t border-white/10 px-4 py-5 sm:px-6 lg:px-8">
        <p className="text-center text-xs text-white/45">
          © {new Date().getFullYear()} SCORAM. All rights reserved.
        </p>
      </div>
    </footer>
  );
}
