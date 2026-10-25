/**
 * The one decorative signature of the product: an eight-point star lattice
 * in the spirit of Shiraz tilework, drawn as a light line pattern.
 */
export function TilePattern({ className, id = "tile" }: { className?: string; id?: string }) {
  return (
    <svg className={className} aria-hidden="true" focusable="false" width="100%" height="100%">
      <defs>
        <pattern id={id} width="56" height="56" patternUnits="userSpaceOnUse">
          <g fill="none" stroke="var(--tile-line)" strokeWidth="1.2">
            <path d="M28 6 L34 22 L50 28 L34 34 L28 50 L22 34 L6 28 L22 22 Z" />
            <path d="M12.5 12.5 L28 18.5 L43.5 12.5 L37.5 28 L43.5 43.5 L28 37.5 L12.5 43.5 L18.5 28 Z" />
            <circle cx="28" cy="28" r="4" />
            <path d="M0 0 L6 6 M56 0 L50 6 M0 56 L6 50 M56 56 L50 50" />
          </g>
        </pattern>
      </defs>
      <rect width="100%" height="100%" fill={`url(#${id})`} />
    </svg>
  );
}
