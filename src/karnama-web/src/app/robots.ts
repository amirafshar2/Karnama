import type { MetadataRoute } from "next";
import { SITE_URL } from "@/lib/site";

export const dynamic = "force-static";

/** The public site is open to crawlers; the panel, login and API are not. */
export default function robots(): MetadataRoute.Robots {
  return {
    rules: [{ userAgent: "*", allow: "/", disallow: ["/dashboard", "/login", "/setup", "/work", "/orders", "/attendance", "/clients", "/packages", "/users", "/roles", "/services", "/settings", "/profile", "/publishing", "/content", "/api/"] }],
    sitemap: `${SITE_URL}/sitemap.xml`,
    host: SITE_URL,
  };
}
