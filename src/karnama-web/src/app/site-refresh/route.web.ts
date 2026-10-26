import { revalidatePath } from "next/cache";

/**
 * Called by the public site's "waking up" notice once the API answers again: pages that were rendered while the API was
 * asleep (empty sections) are rebuilt on the next visit instead of waiting for their normal refresh time.
 * Safe to call by anyone: it only acts when the API really answers now, and at most once every 30 seconds.
 */
const API = (process.env.API_INTERNAL_BASE ?? process.env.NEXT_PUBLIC_API_BASE ?? "http://localhost:5080").replace(/\/$/, "");
let last = 0;

export async function POST() {
  if (Date.now() - last < 30_000) return new Response(null, { status: 204 });
  try {
    const r = await fetch(`${API}/api/v1/public/brand`, { cache: "no-store", signal: AbortSignal.timeout(5000) });
    if (!r.ok) return new Response(null, { status: 503 });
  } catch {
    return new Response(null, { status: 503 });
  }
  last = Date.now();
  revalidatePath("/", "layout");
  return new Response(null, { status: 204 });
}
