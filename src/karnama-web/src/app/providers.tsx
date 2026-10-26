"use client";

import { QueryClient, QueryClientProvider } from "@/lib/query";
import { useEffect, useState } from "react";
import dynamic from "next/dynamic";
import { BrandTheme } from "@/components/app/brand-theme";
import { AuthProvider } from "@/lib/auth";
import { ThemeProvider } from "@/lib/theme";
import { I18nProvider } from "@/lib/i18n/provider";
import { ApiError } from "@/lib/api";
import { CLIENT_KIND } from "@/lib/config";
import { wakeApi } from "@/lib/wake";

// Service-worker registration runs after the first paint (keeps it out of the critical path).
const ServiceWorker = dynamic(() => import("@/components/app/pwa").then((m) => m.ServiceWorker), { ssr: false });
// Android only: back button, status bar, notification taps (the website never loads Capacitor).
const NativeShell = dynamic(() => import("@/components/app/native-shell").then((m) => m.NativeShell), { ssr: false });
const AppToaster = dynamic(() => import("@/components/app/app-toaster"), { ssr: false });

/** Free host only (the API on its own address): wake it now and keep it awake while the panel is open and visible. */
function KeepApiAwake() {
  useEffect(() => {
    wakeApi();
    const id = setInterval(() => document.visibilityState === "visible" && wakeApi(), 4 * 60_000);
    return () => clearInterval(id);
  }, []);
  return null;
}

export function Providers({ children }: { children: React.ReactNode }) {
  const [client] = useState(
    () =>
      new QueryClient({
        defaultOptions: {
          queries: {
            staleTime: 30_000,
            refetchOnWindowFocus: false,
            retry: (count, err) => count < 1 && !(err instanceof ApiError && err.status >= 400 && err.status < 500),
          },
        },
      }),
  );
  return (
    <ThemeProvider>
      <QueryClientProvider client={client}>
        <I18nProvider>
          <AuthProvider>
            <BrandTheme />
            {CLIENT_KIND === "web" && <KeepApiAwake />}
            {CLIENT_KIND === "android" ? <NativeShell /> : <ServiceWorker />}
            {children}
            <AppToaster />
          </AuthProvider>
        </I18nProvider>
      </QueryClientProvider>
    </ThemeProvider>
  );
}
