"use client";

import { useI18n } from "@/lib/i18n/provider";
import { useBrand } from "@/lib/use-brand";
import { cn } from "@/lib/utils";

export { useBrand };

/** Agency logo + name, both from Settings > Brand. Without a logo: the first letter on the brand colours. */
export function BrandMark({ className, compact = false }: { className?: string; compact?: boolean }) {
  const brand = useBrand();
  const { t } = useI18n();
  const name = brand.name ?? t("brand.defaultName");
  return (
    <span className={cn("inline-flex items-center gap-2 font-extrabold", className)}>
      {brand.logo ? (
        // eslint-disable-next-line @next/next/no-img-element -- logo comes from the API at runtime (also in the static app build)
        <img src={brand.logo} alt="" width={28} height={28} className="size-7 shrink-0 rounded-md object-contain" />
      ) : (
        <span
          aria-hidden="true"
          className="grid size-7 shrink-0 place-items-center rounded-md border border-border bg-[var(--brand-black)] text-base leading-none font-black text-[var(--brand-yellow)]"
        >
          {name.trim().charAt(0).toUpperCase()}
        </span>
      )}
      {!compact && <span className="truncate">{name}</span>}
    </span>
  );
}
