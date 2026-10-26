"use client";

import { Loader2 } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { api } from "@/lib/api";
import { useI18n } from "@/lib/i18n/provider";
import type { Camera, Live } from "./types";

/**
 * WebRTC (WHEP) straight from the office box through our proxy: video only (no audio is ever requested), a token for
 * this one camera, closed after 2 minutes without any movement on the page so the office upload isn't wasted.
 */
export function LiveView({ camera, onClose }: { camera: Camera; onClose: () => void }) {
  const { t } = useI18n();
  const video = useRef<HTMLVideoElement>(null);
  const [high, setHigh] = useState(false);
  const [state, setState] = useState<"loading" | "playing" | "failed" | "idle">("loading");

  useEffect(() => {
    let pc: RTCPeerConnection | null = null;
    let session: Live | null = null;
    let closed = false;
    let idle = 0;
    const start = async () => {
      try {
        session = await api<Live>(`/api/v1/cctv/cameras/${camera.id}/live?high=${high}`, { method: "POST" });
        pc = new RTCPeerConnection();
        pc.addTransceiver("video", { direction: "recvonly" });
        pc.ontrack = (e) => {
          if (video.current) video.current.srcObject = e.streams[0] ?? new MediaStream([e.track]);
          setState("playing");
        };
        const offer = await pc.createOffer();
        await pc.setLocalDescription(offer);
        await new Promise<void>((resolve) => {
          if (pc!.iceGatheringState === "complete") return resolve();
          const h = window.setTimeout(resolve, 2000);
          pc!.addEventListener("icegatheringstatechange", () => { if (pc!.iceGatheringState === "complete") { window.clearTimeout(h); resolve(); } });
        });
        const res = await fetch(session.whepUrl, { method: "POST", headers: { "Content-Type": "application/sdp", Authorization: `Bearer ${session.token}` }, body: pc.localDescription!.sdp });
        if (!res.ok || closed) throw new Error(String(res.status));
        await pc.setRemoteDescription({ type: "answer", sdp: await res.text() });
        idle = window.setTimeout(() => { setState("idle"); stop(); }, session.idleSeconds * 1000);
      } catch {
        if (!closed) setState("failed");
      }
    };
    const stop = () => {
      pc?.close();
      pc = null;
      if (session) void api(`/api/v1/cctv/cameras/${camera.id}/live/stop`, { method: "POST", body: { sessionId: session.sessionId } }).catch(() => undefined);
      session = null;
    };
    // Any movement on the page keeps it going.
    const activity = () => {
      if (!session) return;
      window.clearTimeout(idle);
      idle = window.setTimeout(() => { setState("idle"); stop(); }, session.idleSeconds * 1000);
    };
    window.addEventListener("pointermove", activity);
    window.addEventListener("keydown", activity);
    window.addEventListener("touchstart", activity);
    void start();
    return () => {
      closed = true;
      window.clearTimeout(idle);
      window.removeEventListener("pointermove", activity);
      window.removeEventListener("keydown", activity);
      window.removeEventListener("touchstart", activity);
      stop();
    };
  }, [camera.id, high]);

  return (
    <Sheet open onOpenChange={(o) => !o && onClose()}>
      <SheetContent side="bottom" className="mx-auto max-w-4xl rounded-t-2xl">
        <SheetHeader className="p-4">
          <SheetTitle>{camera.name}</SheetTitle>
          <SheetDescription>{t("cctv.liveAudit")}</SheetDescription>
        </SheetHeader>
        <div className="relative grid aspect-video place-items-center bg-black text-neutral-200">
          <video ref={video} autoPlay playsInline muted className="size-full object-contain" />
          {state === "loading" && <Loader2 className="absolute size-8 animate-spin" aria-label={t("common.loading")} />}
          {state === "failed" && <p className="absolute p-4 text-center">{t("cctv.liveFailed")}</p>}
          {state === "idle" && <p className="absolute p-4 text-center">{t("cctv.liveIdle")}</p>}
        </div>
        <div className="flex flex-wrap gap-2 p-4">
          <Button variant="outline" className="h-11" onClick={() => { setState("loading"); setHigh(!high); }}>{high ? t("cctv.liveLow") : t("cctv.liveHigh")}</Button>
          <Button className="h-11" onClick={onClose}>{t("cctv.liveStop")}</Button>
        </div>
      </SheetContent>
    </Sheet>
  );
}
