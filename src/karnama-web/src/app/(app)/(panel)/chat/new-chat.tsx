"use client";

import { useQuery } from "@/lib/query";
import { useState } from "react";
import { EditorSheet } from "@/components/app/editor-sheet";
import { TextField, Toggle } from "@/components/app/form";
import { api } from "@/lib/api";
import { useI18n } from "@/lib/i18n/provider";
import { notify } from "@/lib/notify";
import type { Contact, Conversation } from "./types";

/** Start a direct chat (anyone I'm allowed to reach) or create/edit a group (managers). */
export function NewChat({ mode, group, onClose, onOpened }: { mode: "direct" | "group"; group: Conversation | null; onClose: () => void; onOpened: (id: string) => void }) {
  const { t } = useI18n();
  const contacts = useQuery({ queryKey: ["chat-contacts"], queryFn: () => api<Contact[]>("/api/v1/chat/contacts") });
  const [q, setQ] = useState("");
  const [title, setTitle] = useState(group?.title ?? "");
  const [members, setMembers] = useState<string[]>(group?.members.map((m) => m.id) ?? []);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const list = (contacts.data ?? []).filter((c) => (mode === "direct" || !c.isClient) && (!q.trim() || c.name.includes(q.trim()) || (c.note ?? "").includes(q.trim())));

  const open = async (userId: string) => {
    setBusy(true);
    setError(null);
    try {
      onOpened((await api<Conversation>("/api/v1/chat/direct", { method: "POST", body: { userId } })).id);
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  };

  const saveGroup = async () => {
    setBusy(true);
    setError(null);
    try {
      const body = { title, members };
      const c = group
        ? await api<Conversation>(`/api/v1/chat/groups/${group.id}`, { method: "PUT", body })
        : await api<Conversation>("/api/v1/chat/groups", { method: "POST", body });
      notify.success(t("crud.saved"));
      onOpened(c.id);
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  };

  const search = (
    <label className="block">
      <span className="sr-only">{t("chat.contactsSearch")}</span>
      <input type="search" value={q} onChange={(e) => setQ(e.target.value)} placeholder={t("chat.contactsSearch")} className="h-11 w-full rounded-lg border bg-background px-3 text-sm" data-testid="chat-contacts-search" />
    </label>
  );

  if (mode === "direct")
    return (
      <EditorSheet title={t("chat.new")} onClose={onClose} onSubmit={() => {}} busy={busy} error={error} loading={!contacts.data && !contacts.error} testId="chat-new-sheet" noSubmit>
        {search}
        {list.length === 0 && contacts.data && <p className="text-sm text-muted-foreground">{t("chat.noContacts")}</p>}
        <ul className="divide-y rounded-lg border" data-testid="chat-contacts">
          {list.map((c) => (
            <li key={c.id}>
              <button type="button" className="flex min-h-12 w-full items-center justify-between gap-2 px-3 py-2 text-start hover:bg-muted" disabled={busy} onClick={() => open(c.id)} data-testid={`chat-contact-${c.name}`}>
                <span>
                  <span className="block font-medium">{c.name}</span>
                  {c.note && <span className="block text-xs text-muted-foreground">{c.note}</span>}
                </span>
                {c.isClient && <span className="rounded bg-amber-100 px-1.5 text-xs text-amber-950 dark:bg-amber-950 dark:text-amber-100">{t("chat.client")}</span>}
              </button>
            </li>
          ))}
        </ul>
      </EditorSheet>
    );

  return (
    <EditorSheet title={group ? t("chat.editGroup") : t("chat.newGroup")} onClose={onClose} onSubmit={saveGroup} busy={busy} error={error} loading={!contacts.data && !contacts.error} testId="chat-group">
      <TextField label={t("chat.groupTitle")} help={t("chat.groupTitleHelp")} value={title} onChange={setTitle} testId="chat-group-title" />
      <fieldset className="space-y-2">
        <legend className="text-sm font-bold">{t("chat.groupMembers")}</legend>
        <p className="text-sm text-muted-foreground">{t("chat.groupMembersHelp")}</p>
        {search}
        <ul className="max-h-80 divide-y overflow-y-auto rounded-lg border">
          {list.map((c) => (
            <li key={c.id}>
              <label className="flex min-h-12 cursor-pointer items-center gap-3 px-3">
                <input type="checkbox" className="size-5 accent-[var(--foreground)]" checked={members.includes(c.id)}
                  onChange={(e) => setMembers(e.target.checked ? [...members, c.id] : members.filter((x) => x !== c.id))} data-testid={`chat-member-${c.name}`} />
                {c.name}
              </label>
            </li>
          ))}
        </ul>
      </fieldset>
    </EditorSheet>
  );
}

export function ChatSettings({ onClose }: { onClose: () => void }) {
  const { t } = useI18n();
  const current = useQuery({ queryKey: ["chat-settings"], gcTime: 0, queryFn: () => api<{ workerToWorker: boolean; clientChat: boolean }>("/api/v1/settings/chat") });
  const [s, setS] = useState<{ workerToWorker: boolean; clientChat: boolean } | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const v = s ?? current.data;
  const save = async () => {
    if (!v) return;
    setBusy(true);
    try {
      await api("/api/v1/settings/chat", { method: "PUT", body: v });
      notify.success(t("crud.saved"));
      onClose();
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  };
  return (
    <EditorSheet title={t("chat.settings")} onClose={onClose} onSubmit={save} busy={busy} error={error ?? current.error} loading={!v && !current.error} testId="chat-settings-sheet">
      {v && (
        <>
          <Toggle label={t("chat.workerToWorker")} help={t("chat.workerToWorkerHelp")} checked={v.workerToWorker} onChange={(x) => setS({ ...v, workerToWorker: x })} />
          <Toggle label={t("chat.clientChat")} help={t("chat.clientChatHelp")} checked={v.clientChat} onChange={(x) => setS({ ...v, clientChat: x })} />
        </>
      )}
    </EditorSheet>
  );
}
