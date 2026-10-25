"use client";

import { Toaster } from "@/components/ui/sonner";
import { useIsMobile } from "@/hooks/use-mobile";
import { useI18n } from "@/lib/i18n/provider";

/** Loaded lazily (see providers.tsx). Phones: top, so toasts never cover the bottom navigation or row actions. */
export default function AppToaster() {
  const { dir } = useI18n();
  const isMobile = useIsMobile();
  const position = isMobile ? "top-center" : dir === "rtl" ? "bottom-left" : "bottom-right";
  return <Toaster dir={dir} position={position} richColors closeButton visibleToasts={2} />;
}
