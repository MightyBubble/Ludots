/** @type {import('tailwindcss').Config} */
export default {
  content: ["./index.html", "./src/**/*.{js,ts,jsx,tsx}"],
  darkMode: "class",
  theme: {
    extend: {
      colors: {
        bg: { DEFAULT: "#0e0e16", panel: "#14141f", card: "#1a1a28", hover: "#20202f", border: "#2a2a3a" },
        accent: { DEFAULT: "#3b82f6", hover: "#60a5fa", dim: "#3b82f620", glow: "#3b82f640" },
        warn: { DEFAULT: "#f59e0b", bg: "#f59e0b15" },
        err: { DEFAULT: "#ef4444", bg: "#ef444415" },
        ok: { DEFAULT: "#22c55e", bg: "#22c55e15" },
      },
      fontSize: { "2xs": ["0.625rem", { lineHeight: "0.875rem" }] },
    },
  },
  plugins: [],
};
