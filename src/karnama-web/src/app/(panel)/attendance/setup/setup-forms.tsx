"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Plus, Trash2 } from "lucide-react";
import { useState } from "react";
import { useErrorMessage } from "@/components/app/error-text";
import { JalaliDateField, NumberField, TextField, Toggle } from "@/components/app/form";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { api } from "@/lib/api";
import { faCrud } from "@/lib/i18n/fa-crud";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts, type TextKey } from "@/lib/i18n/registry";
import { notify } from "@/lib/notify";
import type { KeysetPage, RoleListItem } from "@/lib/types";

registerTexts(faCrud);


type Day = { day: number; start: string; end: string };
type Settings = { officeNetworks: string[]; autoClockInOnTimer: boolean; offlineToleranceMinutes: number; forgottenAfterHours: number; selfieRetentionDays: number; locationRetentionDays: number; askEarlyLeaveReason: boolean };
export type Setup = {
  settings: Settings;
  schedules: { id: string; userId: string | null; days: Day[]; breakMinutes: number; graceMinutes: number; flexible: boolean; weeklyHours: number }[];
  policies: { id: string; name: string; rule: string; roleIds: string[]; userIds: string[]; isDefault: boolean }[];
  places: { id: string; name: string; latitude: number; longitude: number; radiusMeters: number }[];
  holidays: { id: string; date: string; name: string }[];
  yourIp: string | null;
};

const PRESETS = ["OfficeNetwork & (RotatingQr | BiometricDevice)", "OfficeNetwork | RotatingQr | Geofence", "Geofence & Selfie", "RotatingQr", "ManagerApproval"];

export function SetupForms({ setup }: { setup: Setup }) {
  const { t } = useI18n();
  const qc = useQueryClient();
  const errorMessage = useErrorMessage();
  const refresh = () => qc.invalidateQueries({ queryKey: ["attendance-setup"] });
  const run = async (fn: () => Promise<unknown>) => {
    try {
      await fn();
      notify.success(t("crud.saved"));
      await refresh();
    } catch (e) {
      notify.error(errorMessage(e));
    }
  };

  const def = setup.schedules.find((s) => s.userId === null);
  const [days, setDays] = useState<Record<number, Day | null>>(() => Object.fromEntries([0, 1, 2, 3, 4, 5, 6].map((d) => [d, def?.days.find((x) => x.day === d) ?? null])));
  const [brk, setBrk] = useState<number | null>(def?.breakMinutes ?? 60);
  const [grace, setGrace] = useState<number | null>(def?.graceMinutes ?? 10);
  const [flexible, setFlexible] = useState(def?.flexible ?? false);
  const [weekly, setWeekly] = useState<number | null>(def?.weeklyHours ?? 44);
  const [s, setS] = useState<Settings>(setup.settings);
  const [newIp, setNewIp] = useState("");
  const [place, setPlace] = useState({ name: "", lat: "", lng: "", radius: 150 as number | null });
  const [holiday, setHoliday] = useState<{ date: string | null; name: string }>({ date: null, name: "" });
  const roles = useQuery({ queryKey: ["roles", "options"], queryFn: () => api<KeysetPage<RoleListItem>>("/api/v1/roles?limit=100") });

  const hhmm = (v: string) => (v.length === 5 ? `${v}:00` : v);

  return (
    <>
      <section className="space-y-4 rounded-xl border bg-card p-5" aria-labelledby="sched">
        <h2 id="sched" className="text-lg font-bold">
          {t("setup.att.schedule")}
        </h2>
        <p className="text-sm text-muted-foreground">{t("setup.att.scheduleHelp")}</p>
        <ul className="space-y-2">
          {[0, 1, 2, 3, 4, 5, 6].map((d) => (
            <li key={d} className="flex flex-wrap items-center gap-3">
              <label className="flex min-h-11 w-32 items-center gap-2 text-sm font-medium">
                <input type="checkbox" className="size-5" checked={!!days[d]} onChange={(e) => setDays({ ...days, [d]: e.target.checked ? { day: d, start: "09:00:00", end: "17:00:00" } : null })} />
                {t(`setup.att.day.${d}` as TextKey)}
              </label>
              {days[d] && (
                <>
                  <Input type="time" dir="ltr" className="h-11 w-32" value={days[d]!.start.slice(0, 5)} onChange={(e) => setDays({ ...days, [d]: { ...days[d]!, start: hhmm(e.target.value) } })} aria-label={`${t(`setup.att.day.${d}` as TextKey)} start`} />
                  <span aria-hidden="true">–</span>
                  <Input type="time" dir="ltr" className="h-11 w-32" value={days[d]!.end.slice(0, 5)} onChange={(e) => setDays({ ...days, [d]: { ...days[d]!, end: hhmm(e.target.value) } })} aria-label={`${t(`setup.att.day.${d}` as TextKey)} end`} />
                </>
              )}
            </li>
          ))}
        </ul>
        <div className="grid gap-4 sm:grid-cols-3">
          <NumberField label={t("setup.att.break")} value={brk} onChange={setBrk} min={0} max={240} />
          <NumberField label={t("setup.att.grace")} help={t("setup.att.graceHelp")} value={grace} onChange={setGrace} min={0} max={120} />
          <NumberField label={t("setup.att.weekly")} value={weekly} onChange={setWeekly} min={0} max={80} />
        </div>
        <Toggle label={t("setup.att.flexible")} checked={flexible} onChange={setFlexible} />
        <Button
          className="h-11"
          onClick={() =>
            run(() =>
              api("/api/v1/attendance/setup/schedules", {
                method: "PUT",
                body: { userId: null, days: Object.values(days).filter(Boolean), breakMinutes: brk ?? 0, graceMinutes: grace ?? 0, flexible, weeklyHours: weekly ?? 0 },
              }),
            )
          }
          data-testid="schedule-save"
        >
          {t("common.save")}
        </Button>
      </section>

      <section className="space-y-4 rounded-xl border bg-card p-5" aria-labelledby="net">
        <h2 id="net" className="text-lg font-bold">
          {t("setup.att.network")}
        </h2>
        <p className="text-sm text-muted-foreground">{t("setup.att.networkHelp")}</p>
        <ul className="flex flex-wrap gap-2">
          {s.officeNetworks.map((n) => (
            <li key={n} className="flex items-center gap-1 rounded-md border px-2 py-1 text-sm" dir="ltr">
              {n}
              <button type="button" className="grid size-8 place-items-center text-destructive" aria-label={t("crud.remove")} onClick={() => setS({ ...s, officeNetworks: s.officeNetworks.filter((x) => x !== n) })}>
                <Trash2 className="size-4" aria-hidden="true" />
              </button>
            </li>
          ))}
        </ul>
        <div className="flex flex-wrap gap-2">
          <Input dir="ltr" className="h-11 w-56" placeholder="5.160.10.0/24" value={newIp} onChange={(e) => setNewIp(e.target.value)} aria-label={t("setup.att.network")} />
          <Button variant="outline" className="h-11" onClick={() => newIp.trim() && (setS({ ...s, officeNetworks: [...s.officeNetworks, newIp.trim()] }), setNewIp(""))}>
            <Plus aria-hidden="true" />
            {t("crud.add")}
          </Button>
          {setup.yourIp && !s.officeNetworks.includes(setup.yourIp) && (
            <Button variant="outline" className="h-11" onClick={() => setS({ ...s, officeNetworks: [...s.officeNetworks, setup.yourIp!] })}>
              {t("setup.att.addMyIp", { ip: setup.yourIp })}
            </Button>
          )}
        </div>
        <h3 className="pt-2 font-bold">{t("setup.att.options")}</h3>
        <Toggle label={t("setup.att.autoClockIn")} checked={s.autoClockInOnTimer} onChange={(v) => setS({ ...s, autoClockInOnTimer: v })} />
        <div className="grid gap-4 sm:grid-cols-2">
          <NumberField label={t("setup.att.tolerance")} value={s.offlineToleranceMinutes} onChange={(v) => setS({ ...s, offlineToleranceMinutes: v ?? 10 })} min={1} max={120} />
          <NumberField label={t("setup.att.forgotten")} value={s.forgottenAfterHours} onChange={(v) => setS({ ...s, forgottenAfterHours: v ?? 4 })} min={1} max={24} />
        </div>
        <Button className="h-11" onClick={() => run(() => api("/api/v1/attendance/setup/settings", { method: "PUT", body: s }))} data-testid="settings-save">
          {t("common.save")}
        </Button>
      </section>

      <section className="space-y-4 rounded-xl border bg-card p-5" aria-labelledby="pol">
        <h2 id="pol" className="text-lg font-bold">
          {t("setup.att.policies")}
        </h2>
        <p className="text-sm text-muted-foreground">{t("setup.att.policiesHelp")}</p>
        {setup.policies.map((p) => (
          <PolicyRow key={p.id} policy={p} roles={(roles.data?.items ?? []).filter((r) => r.systemKey !== "client")} onSave={(body) => run(() => api(`/api/v1/attendance/setup/policies/${p.id}`, { method: "PUT", body }))} />
        ))}
        <Button variant="outline" className="h-11" onClick={() => run(() => api("/api/v1/attendance/setup/policies", { method: "POST", body: { name: "قانون جدید", rule: "ManagerApproval", roleIds: [], userIds: [], isDefault: false } }))}>
          <Plus aria-hidden="true" />
          {t("crud.add")}
        </Button>
      </section>

      <section className="space-y-4 rounded-xl border bg-card p-5" aria-labelledby="places">
        <h2 id="places" className="text-lg font-bold">
          {t("setup.att.places")}
        </h2>
        <p className="text-sm text-muted-foreground">{t("setup.att.placesHelp")}</p>
        <ul className="divide-y rounded-lg border">
          {setup.places.map((p) => (
            <li key={p.id} className="flex items-center justify-between gap-2 px-3 py-2 text-sm">
              <span>
                {p.name} <span className="text-xs text-muted-foreground" dir="ltr">({p.latitude.toFixed(5)}, {p.longitude.toFixed(5)}, {p.radiusMeters}m)</span>
              </span>
              <Button variant="ghost" size="icon" className="size-10 text-destructive" aria-label={t("crud.remove")} onClick={() => run(() => api(`/api/v1/attendance/setup/places/${p.id}`, { method: "DELETE" }))}>
                <Trash2 className="size-4" aria-hidden="true" />
              </Button>
            </li>
          ))}
        </ul>
        <div className="grid gap-3 sm:grid-cols-4">
          <TextField label={t("setup.att.placeName")} value={place.name} onChange={(v) => setPlace({ ...place, name: v })} />
          <TextField label="Lat" value={place.lat} onChange={(v) => setPlace({ ...place, lat: v })} dir="ltr" />
          <TextField label="Lng" value={place.lng} onChange={(v) => setPlace({ ...place, lng: v })} dir="ltr" />
          <NumberField label={t("setup.att.radius")} value={place.radius} onChange={(v) => setPlace({ ...place, radius: v })} min={20} max={5000} />
        </div>
        <div className="flex flex-wrap gap-2">
          <Button
            variant="outline"
            className="h-11"
            onClick={() => navigator.geolocation?.getCurrentPosition((p) => setPlace({ ...place, lat: p.coords.latitude.toFixed(6), lng: p.coords.longitude.toFixed(6) }))}
          >
            {t("setup.att.useMyLocation")}
          </Button>
          <Button className="h-11" onClick={() => run(() => api("/api/v1/attendance/setup/places", { method: "POST", body: { name: place.name, latitude: Number(place.lat), longitude: Number(place.lng), radiusMeters: place.radius ?? 150 } }))}>
            <Plus aria-hidden="true" />
            {t("crud.add")}
          </Button>
        </div>
      </section>

      <section className="space-y-4 rounded-xl border bg-card p-5" aria-labelledby="hol">
        <h2 id="hol" className="text-lg font-bold">
          {t("setup.att.holidays")}
        </h2>
        <ul className="divide-y rounded-lg border">
          {setup.holidays.map((h) => (
            <li key={h.id} className="flex items-center justify-between gap-2 px-3 py-2 text-sm">
              <span>{h.name}</span>
              <Button variant="ghost" size="icon" className="size-10 text-destructive" aria-label={t("crud.remove")} onClick={() => run(() => api(`/api/v1/attendance/setup/holidays/${h.id}`, { method: "DELETE" }))}>
                <Trash2 className="size-4" aria-hidden="true" />
              </Button>
            </li>
          ))}
        </ul>
        <div className="grid gap-3 sm:grid-cols-2">
          <JalaliDateField label={t("setup.att.date")} value={holiday.date} onChange={(v) => setHoliday({ ...holiday, date: v })} />
          <TextField label={t("setup.att.holidayName")} value={holiday.name} onChange={(v) => setHoliday({ ...holiday, name: v })} />
        </div>
        <Button className="h-11" onClick={() => run(() => api("/api/v1/attendance/setup/holidays", { method: "POST", body: { date: holiday.date?.slice(0, 10), name: holiday.name } }))}>
          <Plus aria-hidden="true" />
          {t("crud.add")}
        </Button>
      </section>
    </>
  );
}

function PolicyRow({ policy, roles, onSave }: { policy: Setup["policies"][number]; roles: RoleListItem[]; onSave: (body: unknown) => void }) {
  const { t } = useI18n();
  const [name, setName] = useState(policy.name);
  const [rule, setRule] = useState(policy.rule);
  const [roleIds, setRoleIds] = useState(policy.roleIds);
  const [isDefault, setDefault] = useState(policy.isDefault);
  return (
    <div className="space-y-3 rounded-lg border p-4">
      <div className="grid gap-3 sm:grid-cols-2">
        <TextField label={t("setup.att.policyName")} value={name} onChange={setName} />
        <div className="space-y-2">
          <label className="text-sm font-medium" htmlFor={`rule-${policy.id}`}>
            {t("att.howTitle")}
          </label>
          <Input id={`rule-${policy.id}`} dir="ltr" list="rule-presets" className="h-11 font-mono text-sm" value={rule} onChange={(e) => setRule(e.target.value)} />
          <datalist id="rule-presets">
            {PRESETS.map((p) => (
              <option key={p} value={p} />
            ))}
          </datalist>
        </div>
      </div>
      <div className="flex flex-wrap gap-2">
        {roles.map((r) => (
          <label key={r.id} className="flex min-h-10 items-center gap-2 rounded-md border px-2 text-sm">
            <input type="checkbox" className="size-4" checked={roleIds.includes(r.id)} onChange={(e) => setRoleIds(e.target.checked ? [...roleIds, r.id] : roleIds.filter((x) => x !== r.id))} />
            {r.name}
          </label>
        ))}
      </div>
      <Toggle label={t("setup.att.ruleDefault")} checked={isDefault} onChange={setDefault} />
      <Button className="h-10" onClick={() => onSave({ name, rule, roleIds, userIds: policy.userIds, isDefault })}>
        {t("common.save")}
      </Button>
    </div>
  );
}
