"use client";

import { QueryClient, QueryClientProvider } from "@/lib/query";
import { useState } from "react";
import dynamic from "next/dynamic";
import { BrandTheme } from "@/components/app/brand-theme";
import { AuthProvider } from "@/lib/auth";
import { ThemeProvider } from "@/lib/theme";
import { I18nProvider } from "@/lib/i18n/provider";
import { ApiError } from "@/lib/api";

// Service-worker registration runs after the first paint (keeps it out of the critical path).
const ServiceWorker = dynamic(() => import("@/components/app/pwa").then((m) => m.ServiceWorker), { ssr: false });
const AppToaster = dynamic(() => import("@/components/app/app-toaster"), { ssr: false });

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
            <ServiceWorker />
            {children}
            <AppToaster />
          </AuthProvider>
        </I18nProvider>
      </QueryClientProvider>
    </ThemeProvider>
  );
}
