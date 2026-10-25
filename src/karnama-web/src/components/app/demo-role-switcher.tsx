"use client";

import { useQuery } from "@tanstack/react-query";
import { ChevronDown } from "lucide-react";
import { Button } from "@/components/ui/button";
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from "@/components/ui/dropdown-menu";
import { api, type Session } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { useI18n } from "@/lib/i18n/provider";
import type { DemoAccount } from "@/lib/types";

export function DemoRoleSwitcher() {
  const { completeLogin, logout } = useAuth();
  const { t } = useI18n();
  const { data } = useQuery({
    queryKey: ["demo-options"],
    queryFn: () => api<DemoAccount[]>("/api/v1/auth/demo/options", { noAuthRetry: true }),
    staleTime: 60_000,
  });

  const switchTo = async (role: string) => {
    await logout();
    await completeLogin(await api<Session>("/api/v1/auth/demo", { method: "POST", body: { role }, noAuthRetry: true }));
  };

  return (
    !!data?.length && (
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button size="sm" variant="ghost" className="h-12 text-live-foreground hover:bg-black/10" data-testid="demo-switch">
              {t("demo.switchRole")}
              <ChevronDown aria-hidden="true" />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end">
            {data.map((a) => (
              <DropdownMenuItem key={a.key} className="min-h-11" onSelect={() => void switchTo(a.key)}>
                {a.name}
              </DropdownMenuItem>
            ))}
          </DropdownMenuContent>
        </DropdownMenu>
    )
  );
}
