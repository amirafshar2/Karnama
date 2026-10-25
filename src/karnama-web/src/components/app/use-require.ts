"use client";

import { useRouter } from "next/navigation";
import { useEffect } from "react";
import { useAuth } from "@/lib/auth";
import { hasPermission, type Scope } from "@/lib/types";

/** Client-side page guard (the API enforces the same rule; this only avoids showing a page that would fail). */
export function useRequire(anyOf: { code: string; scope: Scope }[]) {
  const { me } = useAuth();
  const router = useRouter();
  const allowed = !!me && anyOf.some((p) => hasPermission(me, p.code, p.scope));
  useEffect(() => {
    if (me && !allowed) router.replace("/dashboard");
  }, [me, allowed, router]);
  return allowed;
}
