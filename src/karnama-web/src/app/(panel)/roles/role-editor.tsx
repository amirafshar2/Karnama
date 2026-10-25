"use client";

import { useQuery } from "@tanstack/react-query";
import { Loader2, Lock } from "lucide-react";
import { useMemo, useState } from "react";
import { ErrorText } from "@/components/app/error-text";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Textarea } from "@/components/ui/textarea";
import { useIsMobile } from "@/hooks/use-mobile";
import { api } from "@/lib/api";
import type { TextKey } from "@/lib/i18n/registry";
import { useI18n } from "@/lib/i18n/provider";
import type { PermissionDefinition, RoleDetail, Scope } from "@/lib/types";
import { cn } from "@/lib/utils";

import { faAccess } from "@/lib/i18n/fa-access";
import { registerTexts } from "@/lib/i18n/registry";

registerTexts(faAccess);

type Props = { id: string | "new" | null; onClose: () => void; onSaved: (created: boolean) => void };

export function RoleEditor({ id, onClose, onSaved }: Props) {
  const { t, dir } = useI18n();
  const isMobile = useIsMobile();
  const open = id !== null;
  const isNew = id === "new";

  const catalog = useQuery({
    queryKey: ["permissions"],
    queryFn: () => api<PermissionDefinition[]>("/api/v1/permissions"),
    enabled: open,
    staleTime: Infinity,
  });
  const detail = useQuery({
    queryKey: ["role", id],
    queryFn: () => api<RoleDetail>(`/api/v1/roles/${id}`),
    enabled: open && !isNew,
    staleTime: 0,
  });

  const side = isMobile ? "bottom" : dir === "rtl" ? "left" : "right";

  return (
    <Sheet open={open} onOpenChange={(o) => !o && onClose()}>
      <SheetContent side={side} className={cn("flex flex-col gap-0 p-0", isMobile ? "h-[92dvh] rounded-t-2xl" : "w-full sm:max-w-xl")}>
        <SheetHeader className="border-b p-5">
          <SheetTitle className="text-xl">{isNew ? t("roles.formCreate") : t("roles.formEdit")}</SheetTitle>
          <SheetDescription>{t("roles.subtitle")}</SheetDescription>
        </SheetHeader>

        {(catalog.isLoading || (!isNew && detail.isLoading)) && (
          <div className="grid flex-1 place-items-center">
            <Loader2 className="animate-spin text-muted-foreground" aria-label={t("common.loading")} />
          </div>
        )}

        {catalog.data && (isNew || detail.data) && (
          <RoleForm
            key={`${id}-${detail.data?.version ?? "new"}`}
            id={isNew ? null : id}
            initial={isNew ? undefined : detail.data}
            catalog={catalog.data}
            onCancel={onClose}
            onSaved={onSaved}
          />
        )}
        <div className="px-5">
          <ErrorText error={catalog.error ?? detail.error} />
        </div>
      </SheetContent>
    </Sheet>
  );
}

function ScopePicker({
  code,
  allowed,
  value,
  disabled,
  onChange,
}: {
  code: string;
  allowed: Scope[];
  value: Scope | null;
  disabled: boolean;
  onChange: (s: Scope | null) => void;
}) {
  const { t } = useI18n();
  const options: (Scope | null)[] = [null, ...allowed];
  return (
    <div role="radiogroup" aria-label={t(`perm.${code}` as TextKey)} className="inline-flex shrink-0 rounded-lg border p-0.5">
      {options.map((s) => {
        const active = value === s;
        return (
          <button
            key={s ?? "none"}
            type="button"
            role="radio"
            aria-checked={active}
            disabled={disabled}
            data-testid={`scope-${code}-${s ?? "none"}`}
            onClick={() => onChange(s)}
            className={cn(
              "min-h-12 md:min-h-10 rounded-md px-2.5 text-xs whitespace-nowrap transition-colors disabled:cursor-not-allowed disabled:opacity-60",
              active ? (s ? "bg-primary font-bold text-primary-foreground" : "bg-muted font-bold") : "text-muted-foreground hover:bg-muted",
            )}
          >
            {t(s ? (`scope.${s}` as TextKey) : "scope.none")}
          </button>
        );
      })}
    </div>
  );
}

function RoleForm({
  id,
  initial,
  catalog,
  onCancel,
  onSaved,
}: {
  id: string | null;
  initial?: RoleDetail;
  catalog: PermissionDefinition[];
  onCancel: () => void;
  onSaved: (created: boolean) => void;
}) {
  const { t, languages } = useI18n();
  const isNew = id === null;
  const [names, setNames] = useState<Record<string, string>>(() => ({ ...(initial?.nameValues ?? {}) }));
  const [description, setDescription] = useState(initial?.description ?? "");
  const [grants, setGrants] = useState<Record<string, Scope>>(() =>
    Object.fromEntries((initial?.permissions ?? []).map((g) => [g.code, g.scope])),
  );
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const locked = !!initial?.isSystem;

  const groups = useMemo(() => {
    const m = new Map<string, PermissionDefinition[]>();
    for (const p of catalog) m.set(p.group, [...(m.get(p.group) ?? []), p]);
    return [...m.entries()];
  }, [catalog]);

  const save = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    const body = {
      name: names,
      description: description || null,
      permissions: Object.entries(grants).map(([code, scope]) => ({ code, scope })),
      version: initial?.version,
    };
    try {
      if (isNew) await api("/api/v1/roles", { method: "POST", body });
      else await api(`/api/v1/roles/${id}`, { method: "PUT", body });
      onSaved(isNew);
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  };

  return (

          <form onSubmit={save} className="flex min-h-0 flex-1 flex-col" noValidate>
            <div className="flex-1 space-y-6 overflow-y-auto p-5">
              {languages.map((l, i) => (
                <div key={l.code} className="space-y-2">
                  <Label htmlFor={`role-name-${l.code}`}>{i === 0 ? t("roles.nameFa") : t("roles.nameIn", { language: l.name })}</Label>
                  <Input
                    id={`role-name-${l.code}`}
                    dir={l.direction}
                    className="h-12 md:h-11"
                    value={names[l.code] ?? ""}
                    required={l.isDefault}
                    maxLength={400}
                    data-testid={`role-name-${l.code}`}
                    onChange={(e) => setNames({ ...names, [l.code]: e.target.value })}
                  />
                </div>
              ))}
              <div className="space-y-2">
                <Label htmlFor="role-description">{t("roles.description")}</Label>
                <Textarea id="role-description" maxLength={500} rows={2} value={description} onChange={(e) => setDescription(e.target.value)} />
              </div>

              <fieldset className="space-y-4">
                <legend className="font-bold">{t("roles.permissions")}</legend>
                {locked && (
                  <p className="flex items-start gap-2 rounded-lg bg-muted p-3 text-sm">
                    <Lock className="mt-0.5 size-4 shrink-0" aria-hidden="true" />
                    {t("roles.systemLocked")}
                  </p>
                )}
                {groups.map(([group, perms]) => (
                  <div key={group} className="rounded-lg border">
                    <p className="border-b bg-muted/50 px-3 py-2 text-sm font-bold">{t(`permGroup.${group}` as TextKey)}</p>
                    <ul className="divide-y">
                      {perms.map((p) => (
                        <li key={p.code} className="flex flex-col gap-2 px-3 py-2.5 sm:flex-row sm:items-center sm:justify-between">
                          <span className="text-sm">{t(`perm.${p.code}` as TextKey)}</span>
                          <ScopePicker
                            code={p.code}
                            allowed={p.allowedScopes}
                            value={grants[p.code] ?? null}
                            disabled={locked}
                            onChange={(s) => {
                              const next = { ...grants };
                              if (s) next[p.code] = s;
                              else delete next[p.code];
                              setGrants(next);
                            }}
                          />
                        </li>
                      ))}
                    </ul>
                  </div>
                ))}
              </fieldset>
            </div>

            {/* Sticky save bar: never hidden under the keyboard or a helper panel. */}
            <div className="safe-bottom sticky bottom-0 border-t bg-background p-4">
              <ErrorText error={error} />
              <div className="mt-2 flex gap-2">
                <Button type="submit" className="h-12 flex-1" disabled={busy} data-testid="role-save">
                  {busy && <Loader2 className="animate-spin" aria-hidden="true" />}
                  {busy ? t("common.saving") : t("common.save")}
                </Button>
                <Button type="button" variant="outline" className="h-12" onClick={onCancel}>
                  {t("common.cancel")}
                </Button>
              </div>
            </div>
          </form>
        
  );
}
