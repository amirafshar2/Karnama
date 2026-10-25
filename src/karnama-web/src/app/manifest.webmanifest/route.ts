// Web app manifest (Android "Install app", iPhone "Add to Home Screen"): name, colours and icon follow
// Settings > Brand. Re-built every 5 minutes on the web server; frozen at build time in the Android bundle.
export const dynamic = "force-static";
export const revalidate = 300;

type Brand = { name: string | null; logoUrl: string | null; colors: { primary: string; dark: string } };

export async function GET() {
  const api = (process.env.API_INTERNAL_BASE ?? process.env.NEXT_PUBLIC_API_BASE ?? "http://localhost:5080").replace(/\/$/, "");
  let brand: Brand | null = null;
  try {
    const res = await fetch(`${api}/api/v1/public/brand`, { signal: AbortSignal.timeout(3000) });
    if (res.ok) brand = (await res.json()) as Brand;
  } catch {
    /* API not reachable (e.g. at build time): use defaults */
  }
  const name = brand?.name ?? "کارنما";
  const dark = brand?.colors.dark ?? "#0A0A0A";
  const icons = [
    { src: "/brand/icon-192.png", sizes: "192x192", type: "image/png" },
    { src: "/brand/icon-512.png", sizes: "512x512", type: "image/png" },
    { src: "/brand/icon-maskable-512.png", sizes: "512x512", type: "image/png", purpose: "maskable" },
  ];
  if (brand?.logoUrl) icons.unshift({ src: api + brand.logoUrl, sizes: "any", type: "image/png" });

  const manifest = {
    id: "/",
    name,
    short_name: name.length > 12 ? name.slice(0, 12) : name,
    description: "مدیریت کار، ساعت کار و سفارش‌های آژانس",
    lang: "fa",
    dir: "rtl",
    start_url: "/dashboard",
    scope: "/",
    display: "standalone",
    orientation: "portrait",
    background_color: dark,
    theme_color: dark,
    icons,
  };
  return new Response(JSON.stringify(manifest), {
    headers: { "Content-Type": "application/manifest+json; charset=utf-8", "Cache-Control": "public, max-age=300" },
  });
}
