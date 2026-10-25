"use client";

import { Loader2, Plus, Search } from "lucide-react";
import { useRouter } from "next/navigation";
import { Fragment, useEffect, useState } from "react";
import { notify } from "@/lib/notify";
import { ErrorText, useErrorMessage } from "@/components/app/error-text";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { useI18n } from "@/lib/i18n/provider";
import { hasPermission, type RoleListItem } from "@/lib/types";
import { useKeysetList } from "@/lib/use-keyset";
import dynamic from "next/dynamic";

import { faRoles } from "@/lib/i18n/fa-roles";
import { registerTexts } from "@/lib/i18n/registry";

registerTexts(faRoles);

// The editor (sheet, dialog primitives, permission matrix) loads only when someone opens it.
const RoleEditor = dynamic(() => import("./role-editor").then((m) => m.RoleEditor), { ssr: false });

function useDebounced<T>(value: T, ms = 300) {
  const [v, setV] = useState(value);
  useEffect(() => {
    const id = setTimeout(() => setV(value), ms);
    return () => clearTimeout(id);
  }, [value, ms]);
  return v;
}

export default function RolesPage() {
  const { me } = useAuth();
  const router = useRouter();
  const { t } = useI18n();
  const errorMessage = useErrorMessage();
  const [search, setSearch] = useState("");
  const [showDeleted, setShowDeleted] = useState(false);
  const [editing, setEditing] = useState<string | "new" | null>(null);
  const [confirming, setConfirming] = useState<string | null>(null);
  const q = useDebounced(search.trim());
  const allowed = hasPermission(me, "roles.manage", "all");

  useEffect(() => {
    if (me && !allowed) router.replace("/dashboard");
  }, [me, allowed, router]);

  const roles = useKeysetList<RoleListItem>("roles", "/api/v1/roles", { limit: "20", q: q || undefined, includeDeleted: showDeleted ? "true" : undefined }, allowed);
  const items = roles.items;
  const refresh = roles.refresh;

  const remove = async (role: RoleListItem) => {
    setConfirming(null);
    try {
      await api(`/api/v1/roles/${role.id}?version=${role.version}`, { method: "DELETE" });
      await refresh();
      notify.message(t("roles.deletedToast", { name: role.name }), {
        duration: 10_000,
        action: {
          label: t("common.restore"),
          onClick: async () => {
            await api(`/api/v1/roles/${role.id}/restore`, { method: "POST" });
            await refresh();
            notify.success(t("roles.restored"));
          },
        },
      });
    } catch (err) {
      notify.error(errorMessage(err));
      await refresh();
    }
  };

  const restore = async (role: RoleListItem) => {
    try {
      await api(`/api/v1/roles/${role.id}/restore`, { method: "POST" });
      await refresh();
      notify.success(t("roles.restored"));
    } catch (err) {
      notify.error(errorMessage(err));
    }
  };

  if (!allowed) return null;

  const Actions = ({ role }: { role: RoleListItem }) =>
    confirming === role.id ? (
      <div className="flex flex-wrap items-center gap-2" role="group" aria-live="polite">
        <span className="text-sm">{t("roles.confirmDelete", { name: role.name })}</span>
        <Button size="sm" variant="destructive" className="h-12 md:h-10" onClick={() => remove(role)} data-testid={`role-confirm-delete-${role.id}`}>
          {t("roles.confirmDeleteYes")}
        </Button>
        <Button size="sm" variant="ghost" className="h-12 md:h-10" onClick={() => setConfirming(null)}>
          {t("common.cancel")}
        </Button>
      </div>
    ) : (
      <div className="flex items-center gap-1">
        {role.isDeleted ? (
          <Button size="sm" variant="outline" className="h-12 md:h-10" onClick={() => restore(role)}>
            {t("common.restore")}
          </Button>
        ) : (
          <>
            <Button size="sm" variant="ghost" className="h-12 md:h-10" onClick={() => setEditing(role.id)} data-testid={`role-edit-${role.id}`}>
              {t("common.edit")}
            </Button>
            {!role.isSystem && (
              <Button size="sm" variant="ghost" className="h-12 md:h-10 text-destructive" onClick={() => setConfirming(role.id)} data-testid={`role-delete-${role.id}`}>
                {t("common.delete")}
              </Button>
            )}
          </>
        )}
      </div>
    );

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-2xl font-extrabold">{t("roles.title")}</h1>
          <p className="mt-1 max-w-prose text-muted-foreground">{t("roles.subtitle")}</p>
        </div>
        <Button className="h-12 md:h-11" onClick={() => setEditing("new")} data-testid="role-new">
          <Plus aria-hidden="true" />
          {t("roles.new")}
        </Button>
      </header>

      <div className="flex flex-wrap items-center gap-4">
        <div className="relative w-full sm:max-w-xs">
          <Search className="pointer-events-none absolute top-1/2 start-3 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden="true" />
          <input
            type="search"
            className="h-12 w-full rounded-md border border-input bg-card ps-9 pe-3 text-base outline-none focus-visible:ring-2 focus-visible:ring-ring md:h-11 md:text-sm"
            placeholder={t("roles.searchPlaceholder")}
            aria-label={t("common.search")}
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            data-testid="role-search"
          />
        </div>
        <label className="flex min-h-12 cursor-pointer items-center gap-2 text-sm font-medium">
          <input
            type="checkbox"
            role="switch"
            checked={showDeleted}
            onChange={(e) => setShowDeleted(e.target.checked)}
            className="size-5 accent-[var(--foreground)]"
            data-testid="role-show-deleted"
          />
          {t("roles.showDeleted")}
        </label>
      </div>

      {roles.isLoading && (
        <div className="space-y-2">
          {Array.from({ length: 4 }, (_, i) => (
            <div key={i} className="h-16 w-full animate-pulse rounded-md bg-muted" />
          ))}
        </div>
      )}
      <ErrorText error={roles.error} />

      {!roles.isLoading && items.length === 0 && !roles.error && (
        <div className="rounded-xl border border-dashed p-8 text-center text-muted-foreground">
          <p>{q ? t("roles.noResults") : t("roles.empty")}</p>
          {!q && (
            <Button className="mt-4 h-11" onClick={() => setEditing("new")}>
              <Plus aria-hidden="true" />
              {t("roles.new")}
            </Button>
          )}
        </div>
      )}

      {items.length > 0 && (
        <>
          {/* Tablet/desktop: table */}
          <div className="hidden overflow-hidden rounded-xl border bg-card md:block">
            <table className="w-full text-sm">
              <thead className="border-b bg-muted/40">
                <tr>
                  <th scope="col" className="h-11 px-4 text-start font-semibold">{t("roles.colName")}</th>
                  <th scope="col" className="h-11 px-4 text-start font-semibold">{t("roles.colPermissions")}</th>
                  <th scope="col" className="h-11 px-4 text-start font-semibold hidden lg:table-cell">{t("roles.colUsers")}</th>
                  <th scope="col" className="h-11 px-4 text-start font-semibold w-0">
                    <span className="sr-only">{t("roles.colActions")}</span>
                  </th>
                </tr>
              </thead>
              <tbody className="divide-y">
                {items.map((role) => (
                  <Fragment key={role.id}>
                  <tr data-testid={`role-row-${role.id}`} className={role.isDeleted ? "opacity-60" : undefined}>
                    <td className="px-4 py-3 align-middle whitespace-normal">
                      <div className="flex flex-wrap items-center gap-2 font-semibold">
                        {role.name}
                        {role.isSystem && <Tag>{t("roles.system")}</Tag>}
                        {role.isDeleted && <Tag outline>{t("roles.deleted")}</Tag>}
                      </div>
                      {role.description && <p className="mt-0.5 text-sm text-muted-foreground">{role.description}</p>}
                    </td>
                    <td className="px-4 py-3 align-middle">{t("roles.permissionCount", { count: role.permissionCount })}</td>
                    <td className="px-4 py-3 align-middle hidden lg:table-cell">{t("roles.userCount", { count: role.userCount })}</td>
                    <td className="px-4 py-3 align-middle">
                      {confirming !== role.id && <Actions role={role} />}
                    </td>
                  </tr>
                  {confirming === role.id && (
                    <tr className="bg-destructive/5">
                      <td colSpan={4} className="px-4 py-3">
                        <Actions role={role} />
                      </td>
                    </tr>
                  )}
                  </Fragment>
                ))}
              </tbody>
            </table>
          </div>

          {/* Mobile: one card per row */}
          <ul className="space-y-3 md:hidden">
            {items.map((role) => (
              <li key={role.id} className={`rounded-xl border bg-card p-4 ${role.isDeleted ? "opacity-60" : ""}`} data-testid={`role-card-${role.id}`}>
                <div className="flex flex-wrap items-center gap-2 font-semibold">
                  {role.name}
                  {role.isSystem && <Tag>{t("roles.system")}</Tag>}
                  {role.isDeleted && <Tag outline>{t("roles.deleted")}</Tag>}
                </div>
                {role.description && <p className="mt-1 text-sm text-muted-foreground">{role.description}</p>}
                <p className="mt-2 flex items-center gap-3 text-sm text-muted-foreground">
                  <span>{t("roles.permissionCount", { count: role.permissionCount })}</span>
                  <span>{t("roles.userCount", { count: role.userCount })}</span>
                </p>
                <div className="mt-3 border-t pt-2">
                  <Actions role={role} />
                </div>
              </li>
            ))}
          </ul>

          {roles.hasMore && (
            <Button variant="outline" className="h-12 md:h-11 w-full" onClick={() => void roles.loadMore()} disabled={roles.loadingMore}>
              {roles.loadingMore && <Loader2 className="animate-spin" aria-hidden="true" />}
              {t("common.loadMore")}
            </Button>
          )}
        </>
      )}

      {editing !== null && <RoleEditor
        id={editing}
        onClose={() => setEditing(null)}
        onSaved={async (created) => {
          setEditing(null);
          await refresh();
          notify.success(created ? t("roles.created") : t("roles.saved"));
        }}
      />}
    </div>
  );
}

function Tag({ children, outline = false }: { children: React.ReactNode; outline?: boolean }) {
  return (
    <span className={`inline-flex items-center rounded-md px-2 py-0.5 text-xs font-medium ${outline ? "border" : "bg-secondary text-secondary-foreground"}`}>
      {children}
    </span>
  );
}
