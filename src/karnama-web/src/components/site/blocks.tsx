import { TilePattern } from "@/components/app/tile-pattern";

/** Arrow used inside orb buttons; points "forward" in RTL. */
export function Arrow() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M15 6l-6 6 6 6" />
    </svg>
  );
}

export function Check() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.6" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M5 12.5l4.5 4.5L19 7.5" />
    </svg>
  );
}

/** Dark band that opens every inner page (the fixed navbar floats over it). */
export function PageHero({ title, lede, children, id }: { title: string; lede?: string | null; children?: React.ReactNode; id?: string }) {
  return (
    <section className="relative isolate overflow-hidden bg-[var(--ink)] pt-36 pb-16 text-[var(--on-ink)] sm:pt-44 sm:pb-24">
      <TilePattern className="absolute inset-0 -z-10 opacity-60 [--tile-line:color-mix(in_srgb,var(--brand-gold)_8%,transparent)] [mask-image:linear-gradient(180deg,#000,transparent)]" id={`ph-${id ?? "tile"}`} />
      <div aria-hidden="true" className="absolute -top-40 start-1/2 -z-10 size-[520px] -translate-x-1/2 rounded-full bg-[var(--hi)] opacity-20 blur-[120px]" />
      <div className="wrap">
        <h1 className="h2 max-w-4xl" data-reveal>{title}</h1>
        {lede && <p className="lede" data-reveal style={{ ["--d" as string]: "120ms" }}>{lede}</p>}
        {children}
      </div>
    </section>
  );
}
