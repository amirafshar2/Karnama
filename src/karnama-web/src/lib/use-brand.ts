"use client";

import { useQuery } from "@tanstack/react-query";
import { api } from "./api";
import { API_BASE } from "./config";
import { DEFAULT_COLORS, type Brand } from "./brand";

export const BRAND_QUERY = ["brand"] as const;

/** Public brand (name, tagline, logo, colours) — shared by every page; refreshed after the admin saves. */
export function useBrand() {
  const { data } = useQuery({
    queryKey: BRAND_QUERY,
    queryFn: () => api<Brand>("/api/v1/public/brand"),
    staleTime: 60_000,
  });
  return {
    loaded: !!data,
    name: data?.name ?? null,
    tagline: data?.tagline ?? null,
    logo: data?.logoUrl ? API_BASE + data.logoUrl : null,
    colors: data?.colors ?? DEFAULT_COLORS,
    version: data?.version ?? 0,
    demo: data?.demo ?? false,
  };
}
