"use client";

import { createContext, useCallback, useContext, useEffect, useState } from "react";

/**
 * Light / dark / system theme in ~1 KB (instead of a library), with a pre-paint script so there's no flash.
 * Stored per browser (a viewer convenience only).
 */
export type ThemeChoice = "light" | "dark" | "system";
const KEY = "karnama.theme";

export const themeInitScript = `(function(){try{var b=localStorage.getItem("karnama.brandcss");if(b){var s=document.createElement("style");s.id="brand-vars";s.textContent=b;document.head.appendChild(s)}var t=localStorage.getItem("${KEY}")||"system";var d=t==="dark"||(t==="system"&&matchMedia("(prefers-color-scheme: dark)").matches);document.documentElement.classList.toggle("dark",d);document.documentElement.style.colorScheme=d?"dark":"light"}catch(e){}})()`;

type Ctx = { theme: ThemeChoice; resolved: "light" | "dark"; setTheme: (t: ThemeChoice) => void };
const ThemeContext = createContext<Ctx>({ theme: "system", resolved: "light", setTheme: () => undefined });

function apply(choice: ThemeChoice) {
  const dark = choice === "dark" || (choice === "system" && matchMedia("(prefers-color-scheme: dark)").matches);
  document.documentElement.classList.toggle("dark", dark);
  document.documentElement.style.colorScheme = dark ? "dark" : "light";
  return dark ? "dark" : "light";
}

export function ThemeProvider({ children }: { children: React.ReactNode }) {
  const [theme, setThemeState] = useState<ThemeChoice>("system");
  const [resolved, setResolved] = useState<"light" | "dark">("light");

  useEffect(() => {
    let stored: ThemeChoice = "system";
    try {
      stored = (localStorage.getItem(KEY) as ThemeChoice) ?? "system";
    } catch {
      /* storage unavailable */
    }
    // Sync React state with what the pre-paint script already applied.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setThemeState(stored);
    setResolved(apply(stored));
    const mql = matchMedia("(prefers-color-scheme: dark)");
    const onChange = () => setResolved(apply((localStorage.getItem(KEY) as ThemeChoice) ?? "system"));
    mql.addEventListener("change", onChange);
    return () => mql.removeEventListener("change", onChange);
  }, []);

  const setTheme = useCallback((t: ThemeChoice) => {
    try {
      localStorage.setItem(KEY, t);
    } catch {
      /* storage unavailable */
    }
    setThemeState(t);
    setResolved(apply(t));
  }, []);

  return <ThemeContext.Provider value={{ theme, resolved, setTheme }}>{children}</ThemeContext.Provider>;
}

export const useTheme = () => useContext(ThemeContext);
