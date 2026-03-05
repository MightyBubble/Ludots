/** @type {import('tailwindcss').Config} */
export default {
  content: ["./index.html", "./src/**/*.{js,ts,jsx,tsx}"],
  darkMode: "class",
  theme: {
    extend: {
      colors: {
        surface: { DEFAULT: "#1a1a2e", light: "#22223a", lighter: "#2a2a44" },
        accent: { DEFAULT: "#0f7dff", hover: "#3399ff", dim: "#0f7dff33" },
      },
    },
  },
  plugins: [],
};
