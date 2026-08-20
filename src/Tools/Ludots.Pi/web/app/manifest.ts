import type { MetadataRoute } from "next";
import { LUDOTS_PI_PRODUCT_DESCRIPTION, LUDOTS_PI_PRODUCT_NAME } from "@/lib/ludots-brand";

export default function manifest(): MetadataRoute.Manifest {
  return {
    id: "/",
    name: LUDOTS_PI_PRODUCT_NAME,
    short_name: LUDOTS_PI_PRODUCT_NAME,
    description: LUDOTS_PI_PRODUCT_DESCRIPTION,
    start_url: "/",
    scope: "/",
    display: "standalone",
    background_color: "#1a1a1a",
    theme_color: "#1a1a1a",
    categories: ["developer", "productivity"],
    lang: "en",
    icons: [
      {
        src: "/icons/icon-192.png",
        sizes: "192x192",
        type: "image/png",
        purpose: "any",
      },
      {
        src: "/icons/icon-512.png",
        sizes: "512x512",
        type: "image/png",
        purpose: "any",
      },
    ],
  };
}
