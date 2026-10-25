"use client";

import { useQueryClient } from "@/lib/query";
import { ArrowRight, Bell, BellOff, CheckCheck, Download, FileText, Loader2, Mic, MoreVertical, Paperclip, Pencil, Play, Send, Square, Trash2, Users, X } from "lucide-react";
import { useCallback, useEffect, useRef, useState } from "react";
import { useErrorMessage } from "@/components/app/error-text";
import { prepareImage } from "@/components/blog/image-prep";
import { Button } from "@/components/ui/button";
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from "@/components/ui/dropdown-menu";
import { api, apiBlob, uploadForm } from "@/lib/api";
import { useI18n } from "@/lib/i18n/provider";
import { notify } from "@/lib/notify";
import { onChange, onTyping, sendTyping } from "@/lib/realtime";
import { cn } from "@/lib/utils";
import type { Conversation, Message, MessagesPage } from "./types";

const MAX_FILE = 20 * 1024 * 1024;
const MAX_VOICE_SECONDS = 120;

/** One open chat: loads the last page, then only what's new (cursor), marks read, shows typing and "seen". */
export function Thread({ conversation: c, isManager, onBack, onEditGroup }: { conversation: Conversation; isManager: boolean; onBack: () => void; onEditGroup: () => void }) {
  const { t } = useI18n();
  const qc = useQueryClient();
  const errorMessage = useErrorMessage();
  const [messages, setMessages] = useState<Message[]>([]);
  const [othersRead, setOthersRead] = useState(0);
  const [hasOlder, setHasOlder] = useState(false);
  const [typing, setTyping] = useState<string | null>(null);
  const [muted, setMuted] = useState(c.muted);
  const bottom = useRef<HTMLDivElement>(null);
  const lastId = useRef(0);
  const typingTimer = useRef<number | undefined>(undefined);

  const merge = useCallback((incoming: Message[], replace = false) => {
    setMessages((old) => {
      const map = new Map((replace ? [] : old).map((m) => [m.id, m]));
      for (const m of incoming) map.set(m.id, m);
      const all = [...map.values()].sort((a, b) => a.id - b.id);
      lastId.current = all.at(-1)?.id ?? 0;
      return all;
    });
  }, []);

  const markRead = useCallback(async (upTo: number) => {
    if (upTo <= 0 || document.hidden) return;
    await api(`/api/v1/chat/${c.id}/read`, { method: "POST", body: { upTo } }).catch(() => undefined);
    void qc.invalidateQueries({ queryKey: ["chat"] });
  }, [c.id, qc]);

  const loadNew = useCallback(async () => {
    const page = await api<MessagesPage>(`/api/v1/chat/${c.id}/messages${lastId.current ? `?after=${lastId.current}` : ""}`);
    setOthersRead(page.othersReadUpTo);
    if (!lastId.current) setHasOlder(page.hasOlder);
    merge(page.items);
    if (page.items.length) {
      void markRead(page.items.at(-1)!.id);
      requestAnimationFrame(() => bottom.current?.scrollIntoView({ block: "end" }));
    }
  }, [c.id, merge, markRead]);

  // Full reload of what's on screen (edits, deletions, read marks).
  const reload = useCallback(async () => {
    const first = messages[0]?.id;
    const page = await api<MessagesPage>(`/api/v1/chat/${c.id}/messages?limit=100${first ? `&before=${lastId.current + 1}` : ""}`);
    setOthersRead(page.othersReadUpTo);
    merge(page.items);
  }, [c.id, merge, messages]);

  useEffect(() => {
    lastId.current = 0;
    void loadNew();
  }, [loadNew]);

  useEffect(() => {
    const off = onChange((ch) => {
      if (ch === "resync") return void loadNew();
      if (ch.kind !== "chat" || ch.id !== c.id) return;
      if (ch.fields.includes("message")) void loadNew();
      else void reload();
    });
    const offTyping = onTyping((tp) => {
      if (tp.conversationId !== c.id) return;
      const who = c.members.find((m) => m.id === tp.userId)?.name ?? "";
      setTyping(who);
      window.clearTimeout(typingTimer.current);
      typingTimer.current = window.setTimeout(() => setTyping(null), 4000);
    });
    return () => {
      off();
      offTyping();
    };
  }, [c.id, c.members, loadNew, reload]);

  const older = async () => {
    const first = messages[0]?.id;
    if (!first) return;
    const page = await api<MessagesPage>(`/api/v1/chat/${c.id}/messages?before=${first}`);
    setHasOlder(page.hasOlder);
    merge(page.items);
  };

  const toggleMute = async () => {
    try {
      await api(`/api/v1/chat/${c.id}/mute`, { method: "POST", body: { muted: !muted } });
      setMuted(!muted);
    } catch (e) {
      notify.error(errorMessage(e));
    }
  };

  const lastMine = [...messages].reverse().find((m) => m.mine && !m.deleted);
  return (
    <div className="flex h-full flex-col" data-testid="chat-thread">
      <header className="flex items-center gap-2 border-b p-2">
        <Button variant="ghost" size="icon" className="size-11 md:hidden" aria-label={t("chat.back")} onClick={onBack} data-testid="chat-back">
          <ArrowRight aria-hidden="true" />
        </Button>
        <div className="min-w-0 flex-1 px-1">
          <h2 className="truncate font-bold">{c.title}</h2>
          <p className="truncate text-xs text-muted-foreground">
            {typing ? t("chat.typing", { name: typing }) : c.kind !== "direct" ? t("chat.members", { count: c.members.length }) : ""}
          </p>
        </div>
        {c.kind === "group" && isManager && (
          <Button variant="ghost" size="icon" className="size-11" aria-label={t("chat.editGroup")} onClick={onEditGroup}>
            <Users aria-hidden="true" />
          </Button>
        )}
        <Button variant="ghost" size="icon" className="size-11" aria-label={muted ? t("chat.unmute") : t("chat.mute")} onClick={toggleMute}>
          {muted ? <BellOff aria-hidden="true" /> : <Bell aria-hidden="true" />}
        </Button>
      </header>

      <div className="min-h-0 flex-1 space-y-2 overflow-y-auto p-3" data-testid="chat-messages" aria-live="polite">
        {hasOlder && (
          <div className="text-center">
            <Button variant="outline" className="h-10" onClick={older}>{t("chat.older")}</Button>
          </div>
        )}
        {messages.map((m) => (
          <Bubble key={m.id} m={m} showAuthor={c.kind !== "direct" && !m.mine} seen={m.id === lastMine?.id && othersRead >= m.id} canModerate={isManager && c.kind !== "direct"} onChanged={reload} />
        ))}
        <div ref={bottom} />
      </div>

      <Composer conversationId={c.id} onSent={(m) => { merge([m]); requestAnimationFrame(() => bottom.current?.scrollIntoView({ block: "end" })); void qc.invalidateQueries({ queryKey: ["chat"] }); }} />
    </div>
  );
}

function Bubble({ m, showAuthor, seen, canModerate, onChanged }: { m: Message; showAuthor: boolean; seen: boolean; canModerate: boolean; onChanged: () => void }) {
  const { t, formatDate } = useI18n();
  const errorMessage = useErrorMessage();
  const [editing, setEditing] = useState<string | null>(null);
  const [confirm, setConfirm] = useState(false);

  const saveEdit = async () => {
    try {
      await api(`/api/v1/chat/messages/${m.id}`, { method: "PUT", body: { body: editing } });
      setEditing(null);
      onChanged();
    } catch (e) {
      notify.error(errorMessage(e));
    }
  };
  const remove = async () => {
    try {
      await api(`/api/v1/chat/messages/${m.id}`, { method: "DELETE" });
      setConfirm(false);
      onChanged();
    } catch (e) {
      notify.error(errorMessage(e));
    }
  };

  return (
    <div className={cn("flex", m.mine ? "justify-start" : "justify-end")} data-testid="chat-message">
      <div className={cn("max-w-[85%] rounded-2xl px-3 py-2 sm:max-w-[70%]", m.mine ? "rounded-ss-sm bg-primary text-primary-foreground" : "rounded-se-sm bg-muted")}>
        {showAuthor && <p className="mb-0.5 text-xs font-bold opacity-80">{m.authorName}</p>}
        {m.deleted ? (
          <p className="text-sm italic opacity-70">{t("chat.deleted")}</p>
        ) : editing !== null ? (
          <div className="space-y-2">
            <textarea value={editing} onChange={(e) => setEditing(e.target.value)} rows={2} className="w-64 max-w-full rounded-md border bg-background p-2 text-sm text-foreground" aria-label={t("chat.edit")} />
            <div className="flex gap-1">
              <Button className="h-9" onClick={saveEdit}>{t("chat.saveEdit")}</Button>
              <Button variant="ghost" className="h-9" onClick={() => setEditing(null)}>{t("common.cancel")}</Button>
            </div>
          </div>
        ) : (
          <>
            {m.kind === "image" && <ImageBody m={m} />}
            {m.kind === "voice" && <VoiceBody m={m} />}
            {m.kind === "file" && <FileBody m={m} />}
            {m.body && <p className="whitespace-pre-wrap break-words text-sm leading-6">{m.body}</p>}
          </>
        )}
        <div className="mt-0.5 flex items-center justify-end gap-1.5 text-[11px] opacity-70">
          {m.editedAt && !m.deleted && <span>{t("chat.edited")}</span>}
          <time dateTime={m.createdAt}>{formatDate(m.createdAt, { hour: "2-digit", minute: "2-digit" })}</time>
          {seen && (
            <span className="inline-flex items-center gap-0.5" data-testid="chat-seen">
              <CheckCheck className="size-3.5" aria-hidden="true" />
              {t("chat.seen")}
            </span>
          )}
          {!m.deleted && (m.mine || canModerate) && editing === null && (
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <button className="-me-1 inline-flex size-7 items-center justify-center rounded-full hover:bg-black/10" aria-label={t("crud.actions")} data-testid="chat-message-menu">
                  <MoreVertical className="size-3.5" aria-hidden="true" />
                </button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end">
                {m.canEdit && (
                  <DropdownMenuItem onSelect={() => setEditing(m.body ?? "")}>
                    <Pencil aria-hidden="true" />
                    {t("chat.edit")}
                  </DropdownMenuItem>
                )}
                <DropdownMenuItem onSelect={() => setConfirm(true)} className="text-destructive">
                  <Trash2 aria-hidden="true" />
                  {t("chat.delete")}
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          )}
        </div>
        {confirm && (
          <div className="mt-2 flex flex-wrap items-center gap-1 text-sm">
            <span>{t("chat.deleteConfirm")}</span>
            <Button variant="destructive" className="h-9" onClick={remove} data-testid="chat-delete-yes">{t("crud.confirmYes")}</Button>
            <Button variant="ghost" className="h-9" onClick={() => setConfirm(false)}>{t("common.cancel")}</Button>
          </div>
        )}
      </div>
    </div>
  );
}

/** Private files come through the API with the token; a blob URL is made only when needed. */
function useBlobUrl(path: string, enabled: boolean) {
  const [url, setUrl] = useState<string | null>(null);
  useEffect(() => {
    if (!enabled) return;
    let made: string | null = null;
    let gone = false;
    apiBlob(path).then((b) => {
      if (gone) return;
      made = URL.createObjectURL(b);
      setUrl(made);
    }).catch(() => undefined);
    return () => {
      gone = true;
      if (made) URL.revokeObjectURL(made);
    };
  }, [path, enabled]);
  return url;
}

function ImageBody({ m }: { m: Message }) {
  const { t } = useI18n();
  const url = useBlobUrl(`/api/v1/chat/files/${m.id}`, true);
  return url ? (
    // eslint-disable-next-line @next/next/no-img-element -- private blob
    <img src={url} alt={m.body ?? t("chat.image")} className="mb-1 max-h-72 rounded-lg object-contain" />
  ) : (
    <div className="mb-1 flex h-32 w-48 items-center justify-center rounded-lg bg-black/10"><Loader2 className="size-5 animate-spin" aria-hidden="true" /></div>
  );
}

function VoiceBody({ m }: { m: Message }) {
  const { t, formatNumber } = useI18n();
  const [load, setLoad] = useState(false);
  const url = useBlobUrl(`/api/v1/chat/files/${m.id}`, load);
  const d = m.durationSeconds ?? 0;
  // Bandwidth (§7): the audio is fetched only when someone presses play.
  return url ? (
    <audio src={url} controls autoPlay className="mb-1 h-10 w-60 max-w-full" />
  ) : (
    <button onClick={() => setLoad(true)} className="mb-1 flex min-h-10 items-center gap-2 rounded-full bg-black/10 px-3 text-sm" aria-label={t("chat.play")} data-testid="chat-voice">
      {load ? <Loader2 className="size-4 animate-spin" aria-hidden="true" /> : <Play className="size-4" aria-hidden="true" />}
      <span dir="ltr">{formatNumber(Math.floor(d / 60))}:{formatNumber(d % 60).padStart(2, formatNumber(0))}</span>
    </button>
  );
}

function FileBody({ m }: { m: Message }) {
  const { t, formatNumber } = useI18n();
  const download = async () => {
    try {
      const b = await apiBlob(`/api/v1/chat/files/${m.id}`);
      const url = URL.createObjectURL(b);
      const a = document.createElement("a");
      a.href = url;
      a.download = m.fileName ?? "file";
      a.click();
      URL.revokeObjectURL(url);
    } catch {
      notify.error(t("common.errorGeneric"));
    }
  };
  return (
    <button onClick={download} className="mb-1 flex max-w-full items-center gap-2 rounded-lg bg-black/10 p-2 text-start text-sm" aria-label={`${t("chat.download")} ${m.fileName ?? ""}`}>
      <FileText className="size-6 shrink-0" aria-hidden="true" />
      <span className="min-w-0">
        <span className="block truncate font-medium" dir="auto">{m.fileName}</span>
        {m.fileSize != null && <span className="block text-xs opacity-70">{formatNumber(Math.max(1, Math.round(m.fileSize / 1024)))} KB</span>}
      </span>
      <Download className="size-4 shrink-0" aria-hidden="true" />
    </button>
  );
}

function Composer({ conversationId, onSent }: { conversationId: string; onSent: (m: Message) => void }) {
  const { t, formatNumber } = useI18n();
  const errorMessage = useErrorMessage();
  const [text, setText] = useState("");
  const [busy, setBusy] = useState(false);
  const [recording, setRecording] = useState<{ seconds: number } | null>(null);
  const recorder = useRef<MediaRecorder | null>(null);
  const chunks = useRef<Blob[]>([]);
  const started = useRef(0);
  const cancelled = useRef(false);
  const tick = useRef<number | undefined>(undefined);
  const lastTyping = useRef(0);
  const fileInput = useRef<HTMLInputElement>(null);

  const sendText = async () => {
    const body = text.trim();
    if (!body || busy) return;
    setBusy(true);
    try {
      const m = await api<Message>(`/api/v1/chat/${conversationId}/messages`, { method: "POST", body: { body, clientKey: crypto.randomUUID() } });
      setText("");
      onSent(m);
    } catch (e) {
      notify.error(errorMessage(e));
    } finally {
      setBusy(false);
    }
  };

  const upload = async (blob: Blob, name: string, voice?: number) => {
    setBusy(true);
    try {
      const form = new FormData();
      form.append("file", blob, name);
      const q = new URLSearchParams({ clientKey: crypto.randomUUID() });
      if (voice !== undefined) {
        q.set("voice", "true");
        q.set("duration", String(voice));
      }
      onSent(await uploadForm<Message>(`/api/v1/chat/${conversationId}/files?${q}`, form));
    } catch (e) {
      notify.error(errorMessage(e));
    } finally {
      setBusy(false);
    }
  };

  const pickFile = async (file: File) => {
    if (file.size > MAX_FILE) return notify.error(t("chat.tooBig"));
    // Photos are shrunk on the phone first (≈150 KB instead of 5–10 MB).
    if (/^image\/(jpeg|png|webp)$/.test(file.type)) {
      try {
        const { blob } = await prepareImage(file, 1280);
        return upload(blob, "photo.webp");
      } catch {
        /* fall back to the original file */
      }
    }
    await upload(file, file.name);
  };

  const startRecording = async () => {
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      const type = ["audio/webm;codecs=opus", "audio/ogg;codecs=opus", "audio/mp4"].find((x) => MediaRecorder.isTypeSupported(x));
      const r = new MediaRecorder(stream, type ? { mimeType: type, audioBitsPerSecond: 24_000 } : undefined);
      chunks.current = [];
      cancelled.current = false;
      r.ondataavailable = (e) => e.data.size && chunks.current.push(e.data);
      r.onstop = () => {
        stream.getTracks().forEach((tr) => tr.stop());
        window.clearInterval(tick.current);
        const seconds = Math.max(1, Math.round((Date.now() - started.current) / 1000));
        setRecording(null);
        if (cancelled.current) return;
        const blob = new Blob(chunks.current, { type: r.mimeType });
        void upload(blob, r.mimeType.includes("mp4") ? "voice.m4a" : r.mimeType.includes("ogg") ? "voice.ogg" : "voice.webm", Math.min(seconds, MAX_VOICE_SECONDS));
      };
      recorder.current = r;
      started.current = Date.now();
      r.start(1000);
      setRecording({ seconds: 0 });
      tick.current = window.setInterval(() => {
        const s = Math.round((Date.now() - started.current) / 1000);
        setRecording({ seconds: s });
        if (s >= MAX_VOICE_SECONDS) r.stop();
      }, 500);
    } catch {
      notify.error(t("chat.micDenied"));
    }
  };

  const stop = (cancel: boolean) => {
    cancelled.current = cancel;
    if (recorder.current?.state === "recording") recorder.current.stop();
  };

  useEffect(() => () => {
    cancelled.current = true;
    if (recorder.current?.state === "recording") recorder.current.stop();
  }, []);

  if (recording) {
    const s = recording.seconds;
    return (
      <div className="flex items-center gap-2 border-t p-2" data-testid="chat-recording">
        <Button variant="ghost" size="icon" className="size-11" aria-label={t("chat.cancelRecord")} onClick={() => stop(true)}>
          <X aria-hidden="true" />
        </Button>
        <p className="flex-1 text-sm">
          <span className="me-2 inline-block size-2.5 animate-pulse rounded-full bg-destructive" aria-hidden="true" />
          {t("chat.recording", { time: `${formatNumber(Math.floor(s / 60))}:${formatNumber(s % 60).padStart(2, formatNumber(0))}` })}
        </p>
        <Button size="icon" className="size-11" aria-label={t("chat.stopRecord")} onClick={() => stop(false)}>
          <Square aria-hidden="true" />
        </Button>
      </div>
    );
  }

  return (
    <form className="flex items-end gap-1 border-t p-2" onSubmit={(e) => { e.preventDefault(); void sendText(); }}>
      <input ref={fileInput} type="file" className="hidden" onChange={(e) => { const f = e.target.files?.[0]; e.target.value = ""; if (f) void pickFile(f); }} data-testid="chat-file" />
      <Button type="button" variant="ghost" size="icon" className="size-11 shrink-0" aria-label={t("chat.attach")} disabled={busy} onClick={() => fileInput.current?.click()}>
        <Paperclip aria-hidden="true" />
      </Button>
      <textarea
        value={text}
        onChange={(e) => {
          setText(e.target.value);
          if (Date.now() - lastTyping.current > 3000) {
            lastTyping.current = Date.now();
            sendTyping(conversationId);
          }
        }}
        onKeyDown={(e) => {
          if (e.key === "Enter" && !e.shiftKey && !e.nativeEvent.isComposing) {
            e.preventDefault();
            void sendText();
          }
        }}
        rows={1}
        placeholder={t("chat.placeholder")}
        aria-label={t("chat.placeholder")}
        className="max-h-32 min-h-11 flex-1 resize-none rounded-xl border bg-background px-3 py-2.5 text-base leading-6 md:text-sm"
        data-testid="chat-input"
      />
      {text.trim() ? (
        <Button type="submit" size="icon" className="size-11 shrink-0" aria-label={t("chat.send")} disabled={busy} data-testid="chat-send">
          {busy ? <Loader2 className="animate-spin" aria-hidden="true" /> : <Send className="rtl:-scale-x-100" aria-hidden="true" />}
        </Button>
      ) : (
        <Button type="button" size="icon" className="size-11 shrink-0" aria-label={t("chat.record")} disabled={busy} onClick={startRecording} data-testid="chat-record">
          {busy ? <Loader2 className="animate-spin" aria-hidden="true" /> : <Mic aria-hidden="true" />}
        </Button>
      )}
    </form>
  );
}
