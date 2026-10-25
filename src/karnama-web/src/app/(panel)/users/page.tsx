"use client";

import dynamic from "next/dynamic";
import { useState } from "react";
import { CrudList, type Column } from "@/components/app/crud-list";
import { useRequire } from "@/components/app/use-require";
import { faM2 } from "@/lib/i18n/fa-m2";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts } from "@/lib/i18n/registry";
import { notify } from "@/lib/notify";
import { useQueryClient } from "@tanstack/react-query";

registerTexts(faM2);

const UserEditor = dynamic(() => import("./user-editor").then((m) => m.UserEditor), { ssr: false });

export type UserItem = {
  id: string;
  displayName: string;
  phone: string;
  isActive: boolean;
  isDemo: boolean;
  twoFactorEnabled: boolean;
  roles: { id: string; name: string; systemKey: string | null }[];
  isDeleted: boolean;
};

export default function UsersPage() {
  const allowed = useRequire([{ code: "users.manage", scope: "all" }]);
  const { t } = useI18n();
  const qc = useQueryClient();
  const [editing, setEditing] = useState<UserItem | "new" | null>(null);
  if (!allowed) return null;

  const status = (u: UserItem) => (u.isDeleted ? t("crud.deleted") : u.isActive ? null : t("crud.inactive"));
  const columns: Column<UserItem>[] = [
    {
      header: t("users.name"),
      cell: (u) => (
        <span className="font-semibold">
          {u.displayName} {u.isDemo && <Badge>{t("users.demo")}</Badge>} {status(u) && <Badge outline>{status(u)}</Badge>}
        </span>
      ),
    },
    { header: t("users.colPhone"), cell: (u) => <span dir="ltr">{u.phone}</span> },
    { header: t("users.colRoles"), cell: (u) => u.roles.map((r) => r.name).join("، ") },
  ];

  return (
    <>
      <CrudList<UserItem>
        testId="users"
        title={t("users.title")}
        subtitle={t("users.subtitle")}
        path="/api/v1/users"
        queryKey="users"
        newLabel={t("users.new")}
        onNew={() => setEditing("new")}
        onEdit={(u) => setEditing(u)}
        columns={columns}
        nameOf={(u) => u.displayName}
        emptyText={t("users.empty")}
        exportPath="/api/v1/users/export"
        historyType="User"
        card={(u) => (
          <>
            <div className="font-semibold">
              {u.displayName} {u.isDemo && <Badge>{t("users.demo")}</Badge>} {status(u) && <Badge outline>{status(u)}</Badge>}
            </div>
            <p className="mt-1 text-sm text-muted-foreground" dir="ltr">
              {u.phone}
            </p>
            <p className="mt-1 text-sm">{u.roles.map((r) => r.name).join("، ")}</p>
          </>
        )}
      />
      {editing !== null && (
        <UserEditor
          user={editing === "new" ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={async (created) => {
            setEditing(null);
            await qc.invalidateQueries({ queryKey: ["users"] });
            notify.success(created ? t("crud.created") : t("crud.saved"));
          }}
        />
      )}
    </>
  );
}

export function Badge({ children, outline }: { children: React.ReactNode; outline?: boolean }) {
  return <span className={`ms-1 inline-flex rounded-md px-2 py-0.5 text-xs font-medium ${outline ? "border" : "bg-secondary text-secondary-foreground"}`}>{children}</span>;
}
