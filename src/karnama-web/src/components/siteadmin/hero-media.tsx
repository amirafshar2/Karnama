"use client";

import { Film, ImagePlus, Trash2 } from "lucide-react";
import { useRef, useState } from "react";
import { prepareImage } from "@/components/blog/image-prep";
import { Button } from "@/components/ui/button";
import { mediaUrl } from "@/lib/config";
import { uploadForm } from "@/lib/api";
import { useI18n } from "@/lib/i18n/provider";
import { useErrorMessage } from "@/components/app/error-text";
import { notify } from "@/lib/notify";

type Value = { heroVideo?: string | null; heroPoster?: string | null; heroImages?: string[] | null };
const url = (id: string) => mediaUrl(`/media/public/${id}`);
const MAX_VIDEO = 18 * 1024 * 1024;

/**
 * The home page's first screen: a short muted video loop behind the headline (MP4/WebM, ≤18 MB),
 * its still for slow connections, and up to six post photos fanned out under the buttons.
 */
export function HeroMedia({ value, onChange }: { value: Value; onChange: (v: Value) => void }) {
  const { t } = useI18n();
  const errorMessage = useErrorMessage();
  const [busy, setBusy] = useState<string | null>(null);
  const videoInput = useRef<HTMLInputElement>(null);
  const posterInput = useRef<HTMLInputElement>(null);
  const photoInput = useRef<HTMLInputElement>(null);
  const images = value.heroImages ?? [];

  const send = async (blob: Blob, name: string, video: boolean) => {
    const form = new FormData();
    form.append("file", blob, name);
    return (await uploadForm<{ id: string }>(`/api/v1/site/media${video ? "?video=true" : ""}`, form)).id;
  };
  const run = async (what: string, job: () => Promise<void>) => {
    setBusy(what);
    try {
      await job();
    } catch (e) {
      notify.error(errorMessage(e));
    } finally {
      setBusy(null);
    }
  };

  return (
    <div className="space-y-5" data-testid="hero-media">
      <div className="grid gap-4 sm:grid-cols-2">
        <div className="space-y-2">
          <p className="text-sm font-bold">{t("home.heroVideo")}</p>
          <p className="text-xs leading-6 text-muted-foreground">{t("home.heroVideoHelp")}</p>
          {value.heroVideo ? (
            <div className="relative overflow-hidden rounded-xl border bg-black">
              <video src={url(value.heroVideo)} muted loop autoPlay playsInline className="aspect-video w-full object-cover" />
              <Button type="button" size="sm" variant="secondary" className="absolute top-2 end-2" onClick={() => onChange({ ...value, heroVideo: null })} aria-label={t("home.remove")}>
                <Trash2 className="size-4" aria-hidden="true" />
              </Button>
            </div>
          ) : (
            <Button type="button" variant="outline" className="h-24 w-full border-dashed" disabled={busy !== null} onClick={() => videoInput.current?.click()}>
              <Film aria-hidden="true" />
              {busy === "video" ? t("home.uploading") : t("home.heroVideoPick")}
            </Button>
          )}
          <input
            ref={videoInput}
            type="file"
            accept="video/mp4,video/webm"
            hidden
            onChange={(e) => {
              const f = e.target.files?.[0];
              e.target.value = "";
              if (!f) return;
              if (f.size > MAX_VIDEO) return notify.error(t("home.heroVideoTooBig"));
              void run("video", async () => onChange({ ...value, heroVideo: await send(f, f.name, true) }));
            }}
          />
        </div>
        <div className="space-y-2">
          <p className="text-sm font-bold">{t("home.heroPoster")}</p>
          <p className="text-xs leading-6 text-muted-foreground">{t("home.heroPosterHelp")}</p>
          {value.heroPoster ? (
            <div className="relative overflow-hidden rounded-xl border">
              {/* eslint-disable-next-line @next/next/no-img-element -- uploaded still */}
              <img src={url(value.heroPoster)} alt="" className="aspect-video w-full object-cover" />
              <Button type="button" size="sm" variant="secondary" className="absolute top-2 end-2" onClick={() => onChange({ ...value, heroPoster: null })} aria-label={t("home.remove")}>
                <Trash2 className="size-4" aria-hidden="true" />
              </Button>
            </div>
          ) : (
            <Button type="button" variant="outline" className="h-24 w-full border-dashed" disabled={busy !== null} onClick={() => posterInput.current?.click()}>
              <ImagePlus aria-hidden="true" />
              {busy === "poster" ? t("home.uploading") : t("home.heroPosterPick")}
            </Button>
          )}
          <input
            ref={posterInput}
            type="file"
            accept="image/*"
            hidden
            onChange={(e) => {
              const f = e.target.files?.[0];
              e.target.value = "";
              if (f) void run("poster", async () => onChange({ ...value, heroPoster: await send((await prepareImage(f, 1920)).blob, "poster.webp", false) }));
            }}
          />
        </div>
      </div>

      <div className="space-y-2">
        <p className="text-sm font-bold">{t("home.heroImages")}</p>
        <p className="text-xs leading-6 text-muted-foreground">{t("home.heroImagesHelp")}</p>
        <ul className="grid grid-cols-3 gap-2 sm:grid-cols-6">
          {images.map((id, i) => (
            <li key={id} className="group relative aspect-[3/4] overflow-hidden rounded-lg border">
              {/* eslint-disable-next-line @next/next/no-img-element -- uploaded photo */}
              <img src={url(id)} alt="" className="size-full object-cover" />
              <button
                type="button"
                className="absolute top-1 end-1 grid size-8 place-items-center rounded-full bg-black/70 text-white"
                onClick={() => onChange({ ...value, heroImages: images.filter((_, j) => j !== i) })}
                aria-label={t("home.remove")}
              >
                <Trash2 className="size-4" aria-hidden="true" />
              </button>
            </li>
          ))}
          {images.length < 6 && (
            <li>
              <button
                type="button"
                disabled={busy !== null}
                onClick={() => photoInput.current?.click()}
                className="grid aspect-[3/4] w-full place-items-center rounded-lg border-2 border-dashed text-muted-foreground hover:border-foreground hover:text-foreground"
                aria-label={t("home.heroImagesAdd")}
              >
                {busy === "photo" ? "…" : <ImagePlus aria-hidden="true" />}
              </button>
            </li>
          )}
        </ul>
        <input
          ref={photoInput}
          type="file"
          accept="image/*"
          multiple
          hidden
          onChange={(e) => {
            const files = [...(e.target.files ?? [])].slice(0, 6 - images.length);
            e.target.value = "";
            if (files.length === 0) return;
            void run("photo", async () => {
              const ids: string[] = [];
              for (const f of files) ids.push(await send((await prepareImage(f, 1080)).blob, "post.webp", false));
              onChange({ ...value, heroImages: [...images, ...ids] });
            });
          }}
        />
      </div>
    </div>
  );
}
