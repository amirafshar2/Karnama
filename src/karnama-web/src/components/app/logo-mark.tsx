/**
 * Karnama logo mark: the outline of an eight-point Shiraz tile star (the product's visual signature) framing a
 * "play" — work that gets shown. Navy plate (trust), camel-gold star (heritage), brand-blue play (impact). Drawn with the brand variables, so it follows Settings › Brand colour changes.
 * An uploaded logo (Settings › Brand) replaces it everywhere.
 */
export const STAR_OUT = "50.0,12.03 61.13,23.12 76.85,23.15 76.88,38.87 87.97,50.0 76.88,61.13 76.85,76.85 61.13,76.88 50.0,87.97 38.87,76.88 23.15,76.85 23.12,61.13 12.03,50.0 23.12,38.87 23.15,23.15 38.87,23.12";
export const STAR_IN = "50.0,20.85 58.55,29.36 70.61,29.39 70.64,41.45 79.15,50.0 70.64,58.55 70.61,70.61 58.55,70.64 50.0,79.15 41.45,70.64 29.39,70.61 29.36,58.55 20.85,50.0 29.36,41.45 29.39,29.39 41.45,29.36";
export const PLAY = "M44 38 L44 62 L64 50 Z";

export function LogoMark({ size = 32, className }: { size?: number; className?: string }) {
  return (
    <svg width={size} height={size} viewBox="0 0 100 100" className={className} aria-hidden="true" focusable="false">
      <rect width="100" height="100" rx="24" fill="var(--brand-black, #0b1b34)" />
      <polygon points={STAR_OUT} fill="var(--brand-gold, #d4bc8b)" />
      <polygon points={STAR_IN} fill="var(--brand-black, #0b1b34)" />
      <path d={PLAY} fill="var(--brand-yellow, #3b6cff)" />
    </svg>
  );
}
