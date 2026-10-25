import Link from "next/link";
import { TilePattern } from "@/components/app/tile-pattern";
import { faCore } from "@/lib/i18n/fa";
import { NotFoundTexts } from "./not-found-texts";

/**
 * Part of every route's bundle, so it stays tiny: rendered with the built-in texts, and a few lines of script swap in
 * the admin's edits (an async server component here makes Next's prefetch requests hang).
 */
export default function NotFound() {
  return (
    <main className="relative grid min-h-dvh place-items-center bg-background px-4 text-foreground">
      <TilePattern className="absolute inset-0 opacity-60" id="nf-tile" />
      <div className="relative max-w-sm rounded-xl border bg-card p-8 text-center shadow-sm">
        <h1 className="text-2xl font-extrabold" data-text="notFound.title">{faCore["notFound.title"]}</h1>
        <p className="mt-2 text-muted-foreground" data-text="notFound.body">{faCore["notFound.body"]}</p>
        <Link href="/" className="mt-6 grid h-12 w-full place-items-center rounded-md border-2 border-[var(--primary-border)] bg-primary font-bold text-primary-foreground" data-text="notFound.home">
          {faCore["notFound.home"]}
        </Link>
      </div>
      <NotFoundTexts />
    </main>
  );
}
