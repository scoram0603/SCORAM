/** @type {import('tailwindcss').Config} */
export default {
  content: ["./index.html", "./src/**/*.{js,jsx}"],
  theme: {
    extend: {
      fontFamily: {
        sans: ["Inter", "system-ui", "-apple-system", "Segoe UI", "sans-serif"],
      },
      colors: {
        // Primary deep navy — brand, headings, sidebar active states.
        // SCORAM brand palette: 500 = Primary Navy (#123B7A), 600 = Dark Navy (#0B2D5C) --
        // exact brand hexes from the design system brief, used for hover/active states darker
        // than 500.
        primary: {
          50: "#EAEEF6",
          100: "#CBD5E9",
          400: "#33538C",
          500: "#123B7A",
          600: "#0B2D5C",
          700: "#081F4D",
          900: "#050F27",
        },
        // Secondary blue — links, CTAs, interactive accents
        secondary: {
          50: "#EAF1FD",
          100: "#CFE1FA",
          500: "#1E63D5",
          600: "#1A53B3",
        },
        // Accent orange — SCORAM brand orange (#FF6B00). Used sparingly per the brand brief:
        // important highlights, primary CTAs, badges -- not painted across the whole UI.
        accent: {
          50: "#FFF1E3",
          100: "#FFDDBB",
          500: "#FF6B00",
          600: "#E35F00",
        },
        mint: { 50: "#E6F7ED", 100: "#C3EBD3", 500: "#1E9E5A" },
        violet: { 50: "#F1EAFB", 500: "#7C3AED" },
        // SCORAM brand teal (#13A6B2) -- secondary actions, positive states, community/discussion
        // accents, per the brand brief.
        teal: { 50: "#E1F7F5", 500: "#13A6B2" },
        ink: { 900: "#111827", 600: "#4B5563", 400: "#9CA3AF" },
        surface: "#F5F7FB",
      },
      boxShadow: {
        card: "0 8px 24px rgba(10, 42, 102, 0.08)",
        cardHover: "0 12px 32px rgba(10, 42, 102, 0.14)",
        floating: "0 8px 20px rgba(30, 99, 213, 0.35)",
      },
      borderRadius: {
        xl2: "20px",
      },
      spacing: {
        section: "24px",
      },
    },
  },
  plugins: [],
};
