import { icons, Sparkles } from "lucide-react";

/** Service icons are stored by name ("pen-line"); rendered on the server, so the icon set never reaches the browser. */
export function ServiceIcon({ name, className }: { name: string | null; className?: string }) {
  const key = (name ?? "").split("-").map((p) => p.charAt(0).toUpperCase() + p.slice(1)).join("") as keyof typeof icons;
  const Icon = icons[key] ?? Sparkles;
  return <Icon className={className} aria-hidden="true" />;
}
