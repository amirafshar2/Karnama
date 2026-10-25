"use client";

import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { EditorSheet } from "@/components/app/editor-sheet";
import { TextField, Toggle } from "@/components/app/form";
import { api } from "@/lib/api";
import { useI18n } from "@/lib/i18n/provider";
import type { KeysetPage, RoleListItem } from "@/lib/types";
import type { UserItem } from "./page";
import { faM2Forms } from "@/lib/i18n/fa-m2-forms";
import { registerTexts as registerFormTexts } from "@/lib/i18n/registry";

registerFormTexts(faM2Forms);

export function UserEditor({ user, onClose, onSaved }: { user: UserItem | null; onClose: () => void; onSaved: (created: boolean) => void }) {
  const { t } = useI18n();
  const roles = useQuery({ queryKey: ["roles", "all-for-users"], queryFn: () => api<KeysetPage<RoleListItem>>("/api/v1/roles?limit=100") });
  const [name, setName] = useState(user?.displayName ?? "");
  const [phone, setPhone] = useState(user?.phone ?? "");
  const [active, setActive] = useState(user?.isActive ?? true);
  const [roleIds, setRoleIds] = useState<string[]>(user?.roles.map((r) => r.id) ?? []);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);

  const submit = async () => {
    setBusy(true);
    setError(null);
    try {
      const body = { displayName: name, phone, roleIds, isActive: active };
      if (user) await api(`/api/v1/users/${user.id}`, { method: "PUT", body });
      else await api(`/api/v1/users`, { method: "POST", body });
      onSaved(!user);
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  };

  return (
    <EditorSheet title={user ? t("users.formEdit") : t("users.formCreate")} onClose={onClose} onSubmit={submit} busy={busy} error={error} testId="user-editor">
      <TextField label={t("users.name")} help={t("users.nameHelp")} value={name} onChange={setName} testId="user-name" autoFocus />
      <TextField label={t("users.phone")} help={t("users.phoneHelp")} value={phone} onChange={setPhone} testId="user-phone" dir="ltr" inputMode="tel" disabled={!!user} />
      <fieldset className="space-y-2">
        <legend className="text-sm font-medium">{t("users.roles")}</legend>
        <p className="text-sm text-muted-foreground">{t("users.rolesHelp")}</p>
        <div className="grid gap-1 sm:grid-cols-2">
          {(roles.data?.items ?? [])
            .filter((r) => r.systemKey !== "client")
            .map((r) => (
              <label key={r.id} className="flex min-h-12 cursor-pointer items-center gap-3 rounded-md border px-3 text-sm">
                <input
                  type="checkbox"
                  className="size-5"
                  checked={roleIds.includes(r.id)}
                  onChange={(e) => setRoleIds((ids) => (e.target.checked ? [...ids, r.id] : ids.filter((x) => x !== r.id)))}
                  data-testid={`user-role-${r.systemKey ?? r.id}`}
                />
                {r.name}
              </label>
            ))}
        </div>
      </fieldset>
      <Toggle label={t("users.active")} help={t("users.activeHelp")} checked={active} onChange={setActive} testId="user-active" />
    </EditorSheet>
  );
}
