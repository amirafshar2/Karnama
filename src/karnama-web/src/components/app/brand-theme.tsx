"use client";

import { useEffect } from "react";
import { brandCss } from "@/lib/brand";
import { useBrand } from "@/lib/use-brand";

export const BRAND_CSS_KEY = "karnama.brandcss";

/**
 * Applies the admin's brand to every page: colour variables, tab title, favicon and the iOS home-screen icon.
 * The CSS is remembered per browser so the next visit paints in brand colours before the API answers.
 */
export function BrandTheme() {
  const brand = useBrand();
  const css = brand.loaded ? brandCss(brand.colors) : null;

  useEffect(() => {
    if (!css) return;
    let el = document.getElementById("brand-vars") as HTMLStyleElement | null;
    if (!el) {
      el = document.createElement("style");
      el.id = "brand-vars";
      document.head.appendChild(el);
    }
    el.textContent = css;
    try {
      localStorage.setItem(BRAND_CSS_KEY, css);
    } catch {
      /* storage unavailable */
    }
  }, [css]);

  useEffect(() => {
    if (!brand.name) return;
    const base = "کارنما";
    const update = () => {
      if (document.title.includes(base)) document.title = document.title.replace(base, brand.name!);
    };
    update();
    const obs = new MutationObserver(update);
    const titleEl = document.querySelector("title");
    if (titleEl) obs.observe(titleEl, { childList: true });
    return () => obs.disconnect();
  }, [brand.name]);

  useEffect(() => {
    if (!brand.logo) return;
    for (const rel of ["icon", "apple-touch-icon"]) {
      let link = document.querySelector<HTMLLinkElement>(`link[rel="${rel}"]`);
      if (!link) {
        link = document.createElement("link");
        link.rel = rel;
        document.head.appendChild(link);
      }
      link.href = brand.logo;
    }
  }, [brand.logo]);

  return null;
}
