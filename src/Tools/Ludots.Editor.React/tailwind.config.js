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
          bg: '#1c1c1e',
          surface: '#2c2c2e',
          elevated: '#3a3a3c',
          fill: '#48484a',
          label: '#f5f5f7',
          secondary: 'rgba(235, 235, 245, 0.6)',
          muted: '#8e8e93',
          silver: '#8e8e93',
          red: '#ff453a',
          yellow: '#ffd60a',
          blue: '#0a84ff',
        },
      },
    },
  },
  plugins: [],
};
