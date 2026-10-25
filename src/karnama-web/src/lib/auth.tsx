"use client";

import { useQueryClient } from "@/lib/query";
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from "react";
import { api, refreshSession, setSessionLostHandler, storeSession, type Session } from "./api";
import { useI18n } from "./i18n/provider";
import { tokenStore } from "./token-store";
import type { Me } from "./types";

type Status = "loading" | "anonymous" | "authenticated";

type AuthContextValue = {
  status: Status;
  me: Me | null;
  /** Called after any successful login step that returned a session. */
  completeLogin: (session: Session) => Promise<Me | null>;
  reloadMe: () => Promise<Me | null>;
  logout: () => Promise<void>;
};

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [status, setStatus] = useState<Status>("loading");
  const [me, setMe] = useState<Me | null>(null);
  const { setLang, lang } = useI18n();
  const langRef = useRef(lang);
  useEffect(() => {
    langRef.current = lang;
  }, [lang]);
  const queryClient = useQueryClient();

  const reloadMe = useCallback(async () => {
    try {
      const m = await api<Me>("/api/v1/me");
      setMe(m);
      setStatus("authenticated");
      if (m.languageCode && m.languageCode !== langRef.current) setLang(m.languageCode);
      return m;
    } catch {
      setMe(null);
      setStatus("anonymous");
      return null;
    }
  }, [setLang]);

  useEffect(() => {
    setSessionLostHandler(() => {
      tokenStore.setAccess(null);
      setMe(null);
      setStatus("anonymous");
    });
    // Restore the session from the refresh cookie (web) or secure storage (android).
    void refreshSession().then(async (ok) => {
      if (ok) await reloadMe();
      else setStatus("anonymous");
    });
  }, [reloadMe]);

  const completeLogin = useCallback(
    async (session: Session) => {
      await storeSession(session);
      return reloadMe();
    },
    [reloadMe],
  );

  const logout = useCallback(async () => {
    try {
      const refresh = await tokenStore.getRefresh();
      await api("/api/v1/auth/logout", { method: "POST", noAuthRetry: true, headers: refresh ? { "X-Refresh-Token": refresh } : undefined });
    } catch {
      /* best effort */
    }
    tokenStore.setAccess(null);
    await tokenStore.setRefresh(null);
    queryClient.clear();
    setMe(null);
    setStatus("anonymous");
  }, [queryClient]);

  const value = useMemo(() => ({ status, me, completeLogin, reloadMe, logout }), [status, me, completeLogin, reloadMe, logout]);
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used inside AuthProvider");
  return ctx;
}
