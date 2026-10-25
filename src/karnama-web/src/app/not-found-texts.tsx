"use client";

import { useEffect } from "react";
import { API_BASE } from "@/lib/config";

/** Applies admin-edited texts (Settings › Texts) to the 404 page. */
export function NotFoundTexts() {
  useEffect(() => {
    fetch(`${API_BASE}/api/v1/i18n/fa`)
      .then((r) => (r.ok ? r.json() : null))
      .then((b: { texts?: Record<string, string> } | null) => {
        for (const el of document.querySelectorAll<HTMLElement>("[data-text]")) {
          const v = b?.texts?.[el.dataset.text!];
          if (v) el.textContent = v;
        }
      })
      .catch(() => undefined);
  }, []);
  return null;
}
