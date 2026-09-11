/** @type {import('tailwindcss').Config} */

export default {
  darkMode: "class",
  content: ["./index.html", "./src/**/*.{js,ts,jsx,tsx}"],
  theme: {
    container: {
      center: true,
    },
    extend: {
      colors: {
        studio: {
          bg: 'var(--studio-bg)',
          surface: 'var(--studio-surface)',
          elevated: 'var(--studio-elevated)',
          fill: 'var(--studio-fill)',
          label: 'var(--studio-label)',
          secondary: 'var(--studio-secondary)',
          muted: 'var(--studio-muted)',
          silver: 'var(--studio-muted)',
          red: 'var(--studio-red)',
          yellow: 'var(--studio-yellow)',
          blue: 'var(--studio-blue)',
        },
      },
    },
  },
  plugins: [],
};
