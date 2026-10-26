"use client";

import { Plus, RefreshCw, Upload } from "lucide-react";
import { useRef, useState } from "react";
import { EditorSheet } from "@/components/app/editor-sheet";
import { ErrorText, useErrorMessage } from "@/components/app/error-text";
import { SelectField, TextField, Toggle } from "@/components/app/form";
import { useRequire } from "@/components/app/use-require";
import { Button } from "@/components/ui/button";
import { api, uploadForm } from "@/lib/api";
import { API_BASE } from "@/lib/config";
import { faCrud } from "@/lib/i18n/fa-crud";
import { faN2 } from "@/lib/i18n/fa-n2";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts, type TextKey } from "@/lib/i18n/registry";
import { notify } from "@/lib/notify";
import { useQuery, useQueryClient } from "@/lib/query";
import { cn } from "@/lib/utils";

registerTexts(faN2);
registerTexts(faCrud);

type Kind = "adms" | "fileImport";
type Mode = "toggle" | "statusKey";
type Device = { id: string; name: string; serial: string; kind: Kind; mode: Mode; location: string | null; allowedIps: string[]; isActive: boolean;
  lastSeenAt: string | null; online: boolean; lastIp: string | null; clockSkewSeconds: number | null; punchesToday: number; pendingCommands: number; version: number; addressSet: boolean };
type Pin = { id: string; pin: string; userId: string; userName: string };
type Unknown = { pin: string; count: number; lastAt: string; deviceName: string };
type Punch = { id: number; deviceName: string; pin: string; userName: string | null; at: string; status: number; outcome: string; note: string | null };
type Person = { id: string; name: string };
type Preview = { headers: string[]; rows: string[][]; totalRows: number; suggested: { pin: number; date: number; time: number | null; status: number | null; hasHeader: boolean } };
type Tab = "devices" | "pins" | "records" | "import";

export default function DevicesPage() {
  const allowed = useRequire([{ code: "devices.manage", scope: "all" }]);
  const { t } = useI18n();
  const [tab, setTab] = useState<Tab>("devices");
  if (!allowed) return null;
  return (
    <div className="space-y-5">
      <header>
        <h1 className="text-2xl font-extrabold">{t("dev.title")}</h1>
        <p className="mt-1 max-w-prose text-muted-foreground">{t("dev.subtitle")}</p>
      </header>
      <div className="flex flex-wrap gap-2" role="tablist">
        {(["devices", "pins", "records", "import"] as Tab[]).map((x) => (
          <button key={x} role="tab" aria-selected={tab === x} onClick={() => setTab(x)} data-testid={`dev-tab-${x}`}
            className={cn("min-h-11 rounded-full border-2 px-4 text-sm font-semibold", tab === x ? "border-foreground bg-foreground text-background" : "border-border hover:border-foreground")}>
            {t(`dev.tab.${x}` as TextKey)}
          </button>
        ))}
      </div>
      {tab === "devices" && <Devices />}
      {tab === "pins" && <Pins />}
      {tab === "records" && <Records />}
      {tab === "import" && <Import />}
    </div>
  );
}

function Devices() {
  const { t, formatNumber, formatDate } = useI18n();
  const qc = useQueryClient();
  const errorMessage = useErrorMessage();
  const q = useQuery({ queryKey: ["devices"], queryFn: () => api<Device[]>("/api/v1/devices"), refetchInterval: 15_000 });
  const [editing, setEditing] = useState<Device | "new" | null>(null);
  const url = (() => { try { return new URL(API_BASE || window.location.origin); } catch { return null; } })();
  const sync = async (d: Device) => {
    try {
      await api(`/api/v1/devices/${d.id}/sync-time`, { method: "POST" });
      notify.success(t("dev.syncQueued"));
      await qc.invalidateQueries({ queryKey: ["devices"] });
    } catch (e) {
      notify.error(errorMessage(e));
    }
  };
  if (q.error) return <ErrorText error={q.error} />;
  return (
    <div className="space-y-4">
      <Button className="h-12 md:h-11" onClick={() => setEditing("new")} data-testid="dev-new"><Plus aria-hidden="true" />{t("dev.new")}</Button>
      {q.data?.length === 0 && <p className="rounded-xl border border-dashed p-6 text-center text-muted-foreground">{t("dev.empty")}</p>}
      <ul className="grid gap-3 md:grid-cols-2" data-testid="dev-list">
        {(q.data ?? []).map((d) => (
          <li key={d.id} className="space-y-2 rounded-xl border bg-card p-4">
            <div className="flex items-start justify-between gap-2">
              <div>
                <p className="font-bold">{d.name}</p>
                <p className="text-xs text-muted-foreground" dir="ltr">{d.kind === "adms" ? d.serial : t("dev.fileKind")}</p>
              </div>
              {d.kind === "adms" && (
                <span className={cn("rounded px-2 py-0.5 text-xs font-semibold", d.online ? "bg-emerald-100 text-emerald-950 dark:bg-emerald-950 dark:text-emerald-100" : "bg-muted")}>
                  {d.online ? t("dev.online") : t("dev.offline")}
                </span>
              )}
            </div>
            <p className="text-sm">
              {d.lastSeenAt ? t("dev.lastSeen", { when: formatDate(d.lastSeenAt, { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" }) }) : t(d.kind === "adms" ? "dev.never" : "dev.noFileYet")}
              {" · "}{t("dev.today", { n: formatNumber(d.punchesToday) })}
            </p>
            {d.clockSkewSeconds !== null && Math.abs(d.clockSkewSeconds) > 120 && (
              <p className="text-sm font-semibold text-destructive">
                {t("dev.skew", { n: formatNumber(Math.round(Math.abs(d.clockSkewSeconds) / 60)), dir: t(d.clockSkewSeconds > 0 ? "dev.ahead" : "dev.behind") })}
              </p>
            )}
            {!d.addressSet && <p className="rounded-lg border border-destructive p-3 text-sm font-semibold text-destructive" data-testid="dev-no-address">{t("dev.noAddress")}</p>}
            {d.kind === "adms" && !d.lastSeenAt && url && (
              <p className="rounded-lg bg-muted/50 p-3 text-xs leading-6">
                <strong className="block">{t("dev.setupTitle")}</strong>
                {t("dev.setupBody", { host: url.hostname, port: url.port || (url.protocol === "https:" ? "443" : "80") })}
              </p>
            )}
            <div className="flex flex-wrap gap-2">
              <Button variant="outline" className="h-11" onClick={() => setEditing(d)}>{t("dev.edit")}</Button>
              {d.kind === "adms" && <Button variant="outline" className="h-11" onClick={() => sync(d)} data-testid="dev-sync"><RefreshCw aria-hidden="true" />{t("dev.syncTime")}</Button>}
            </div>
          </li>
        ))}
      </ul>
      {editing && <DeviceEditor device={editing === "new" ? null : editing} onClose={() => setEditing(null)} onSaved={() => { setEditing(null); void qc.invalidateQueries({ queryKey: ["devices"] }); }} />}
    </div>
  );
}

function DeviceEditor({ device, onClose, onSaved }: { device: Device | null; onClose: () => void; onSaved: () => void }) {
  const { t } = useI18n();
  const [name, setName] = useState(device?.name ?? "");
  const [kind, setKind] = useState<Kind>(device?.kind ?? "adms");
  const [serial, setSerial] = useState(device?.serial ?? "");
  const [mode, setMode] = useState<Mode>(device?.mode ?? "toggle");
  const [location, setLocation] = useState(device?.location ?? "");
  const [ips, setIps] = useState((device?.allowedIps ?? []).join(", "));
  const [active, setActive] = useState(device?.isActive ?? true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const save = async () => {
    setBusy(true);
    setError(null);
    try {
      const body = { name, serial: kind === "adms" ? serial : null, kind, mode, location: location || null, allowedIps: ips.split(/[,،\s]+/).filter(Boolean), isActive: active, version: device?.version };
      await api(device ? `/api/v1/devices/${device.id}` : "/api/v1/devices", { method: device ? "PUT" : "POST", body });
      notify.success(t(device ? "crud.saved" : "crud.created"));
      onSaved();
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  };
  return (
    <EditorSheet title={device ? t("dev.edit") : t("dev.new")} onClose={onClose} onSubmit={save} busy={busy} error={error} testId="dev-editor">
      <TextField label={t("dev.f.name")} help={t("dev.f.nameHelp")} value={name} onChange={setName} testId="dev-name" />
      {!device && (
        <SelectField label={t("dev.f.kind")} help={t("dev.f.kindHelp")} value={kind} onChange={setKind} testId="dev-kind"
          options={(["adms", "fileImport"] as Kind[]).map((k) => ({ value: k, label: t(`dev.kind.${k}` as TextKey) }))} />
      )}
      {kind === "adms" && (
        <>
          <TextField label={t("dev.f.serial")} help={t("dev.f.serialHelp")} value={serial} onChange={setSerial} dir="ltr" disabled={!!device} testId="dev-serial" />
          <SelectField label={t("dev.f.mode")} help={t("dev.f.modeHelp")} value={mode} onChange={setMode}
            options={(["toggle", "statusKey"] as Mode[]).map((k) => ({ value: k, label: t(`dev.mode.${k}` as TextKey) }))} />
          <TextField label={t("dev.f.ips")} help={t("dev.f.ipsHelp")} value={ips} onChange={setIps} optional dir="ltr" />
        </>
      )}
      <TextField label={t("dev.f.location")} help={t("dev.f.locationHelp")} value={location} onChange={setLocation} optional />
      {device && <Toggle label={t("dev.f.active")} help={t("dev.f.activeHelp")} checked={active} onChange={setActive} />}
    </EditorSheet>
  );
}

function Pins() {
  const { t, formatNumber, formatDate } = useI18n();
  const qc = useQueryClient();
  const errorMessage = useErrorMessage();
  const pins = useQuery({ queryKey: ["device-pins"], queryFn: () => api<Pin[]>("/api/v1/devices/pins") });
  const unknown = useQuery({ queryKey: ["device-unknown"], queryFn: () => api<Unknown[]>("/api/v1/devices/unknown-pins") });
  const people = useQuery({ queryKey: ["asset-people"], queryFn: () => api<Person[]>("/api/v1/assets/people") });
  const [pin, setPin] = useState("");
  const [user, setUser] = useState("");
  const [busy, setBusy] = useState(false);
  const refresh = () => Promise.all([qc.invalidateQueries({ queryKey: ["device-pins"] }), qc.invalidateQueries({ queryKey: ["device-unknown"] })]);
  const add = async () => {
    setBusy(true);
    try {
      await api("/api/v1/devices/pins", { method: "POST", body: { pin, userId: user } });
      notify.success(t("dev.pinSaved"));
      setPin("");
      setUser("");
      await refresh();
    } catch (e) {
      notify.error(errorMessage(e));
    } finally {
      setBusy(false);
    }
  };
  const remove = async (id: string) => {
    try {
      await api(`/api/v1/devices/pins/${id}`, { method: "DELETE" });
      await refresh();
    } catch (e) {
      notify.error(errorMessage(e));
    }
  };
  const mapped = new Set((pins.data ?? []).map((p) => p.userId));
  return (
    <div className="grid gap-6 lg:grid-cols-2">
      <section className="space-y-4">
        <form className="space-y-3 rounded-xl border bg-card p-4" onSubmit={(e) => { e.preventDefault(); void add(); }}>
          <TextField label={t("dev.pin")} help={t("dev.pinHelp")} value={pin} onChange={setPin} inputMode="numeric" dir="ltr" testId="dev-pin" />
          <SelectField label={t("dev.person")} help={t("dev.personHelp")} value={user} onChange={setUser} testId="dev-person"
            options={(people.data ?? []).filter((p) => !mapped.has(p.id)).map((p) => ({ value: p.id, label: p.name }))} />
          <Button type="submit" className="h-11" disabled={busy || !pin.trim() || !user} data-testid="dev-pin-add">{t("dev.pinAdd")}</Button>
        </form>
        {pins.error ? <ErrorText error={pins.error} /> : (pins.data ?? []).length === 0 ? <p className="text-sm text-muted-foreground">{t("dev.pinsEmpty")}</p> : (
          <ul className="divide-y rounded-xl border bg-card" data-testid="dev-pins">
            {(pins.data ?? []).map((p) => (
              <li key={p.id} className="flex items-center justify-between gap-2 px-4 py-2">
                <span><strong className="inline-block w-12 tabular-nums">{formatNumber(Number(p.pin))}</strong>{p.userName}</span>
                <Button variant="ghost" className="h-10 text-destructive" onClick={() => remove(p.id)}>{t("dev.pinRemove")}</Button>
              </li>
            ))}
          </ul>
        )}
      </section>
      <section className="space-y-2">
        <h2 className="font-bold">{t("dev.unknown")}</h2>
        <p className="text-sm text-muted-foreground">{t("dev.unknownHelp")}</p>
        <ul className="space-y-2" data-testid="dev-unknown">
          {(unknown.data ?? []).map((u) => (
            <li key={u.pin + u.deviceName}>
              <button type="button" className="w-full rounded-lg border bg-card p-3 text-start text-sm hover:border-foreground" onClick={() => setPin(u.pin)}>
                {t("dev.unknownRow", { pin: formatNumber(Number(u.pin)), n: formatNumber(u.count), when: formatDate(u.lastAt, { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" }), device: u.deviceName })}
              </button>
            </li>
          ))}
        </ul>
      </section>
    </div>
  );
}

function Records() {
  const { t, formatNumber, formatDate } = useI18n();
  const q = useQuery({ queryKey: ["device-punches"], queryFn: () => api<Punch[]>("/api/v1/devices/punches"), refetchInterval: 15_000 });
  if (q.error) return <ErrorText error={q.error} />;
  if (q.data?.length === 0) return <p className="rounded-xl border border-dashed p-6 text-center text-muted-foreground">{t("dev.recordsEmpty")}</p>;
  return (
    <div className="overflow-x-auto rounded-xl border bg-card">
      <table className="w-full min-w-[640px] text-sm" data-testid="dev-records">
        <tbody className="divide-y">
          {(q.data ?? []).map((p) => (
            <tr key={p.id}>
              <td className="px-3 py-2 whitespace-nowrap">{formatDate(p.at, { weekday: "short", day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" })}</td>
              <td className="px-3 py-2">{p.userName ?? `#${formatNumber(Number(p.pin))}`}</td>
              <td className="px-3 py-2 font-semibold">{t(`dev.out.${p.outcome}` as TextKey)}</td>
              <td className="px-3 py-2 text-muted-foreground">{p.note ?? ""}</td>
              <td className="px-3 py-2 text-xs text-muted-foreground">{p.deviceName}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function Import() {
  const { t, formatNumber } = useI18n();
  const qc = useQueryClient();
  const errorMessage = useErrorMessage();
  const devices = useQuery({ queryKey: ["devices"], queryFn: () => api<Device[]>("/api/v1/devices") });
  const fileDevices = (devices.data ?? []).filter((d) => d.kind === "fileImport");
  const input = useRef<HTMLInputElement>(null);
  const [file, setFile] = useState<File | null>(null);
  const [preview, setPreview] = useState<Preview | null>(null);
  const [device, setDevice] = useState("");
  const [map, setMap] = useState<{ pin: string; date: string; time: string; header: boolean }>({ pin: "0", date: "1", time: "", header: true });
  const [busy, setBusy] = useState(false);
  const pick = async (f: File | undefined) => {
    if (!f) return;
    setFile(f);
    setBusy(true);
    try {
      const form = new FormData();
      form.append("file", f, f.name);
      const p = await uploadForm<Preview>("/api/v1/devices/import/preview", form);
      setPreview(p);
      setMap({ pin: String(p.suggested.pin), date: String(p.suggested.date), time: p.suggested.time === null ? "" : String(p.suggested.time), header: p.suggested.hasHeader });
    } catch (e) {
      notify.error(errorMessage(e));
    } finally {
      setBusy(false);
    }
  };
  const run = async () => {
    if (!file || !preview) return;
    setBusy(true);
    try {
      const form = new FormData();
      form.append("file", file, file.name);
      const q = new URLSearchParams({ pin: map.pin, date: map.date, header: String(map.header) });
      if (map.time) q.set("time", map.time);
      const r = await uploadForm<{ added: number; duplicates: number; unknownPins: number; invalid: number; shiftsCreated: number; problems: string[] }>(
        `/api/v1/devices/${device || fileDevices[0]?.id}/import?${q}`, form);
      notify.success(t("dev.import.done", { added: formatNumber(r.added), shifts: formatNumber(r.shiftsCreated), dup: formatNumber(r.duplicates), unknown: formatNumber(r.unknownPins), invalid: formatNumber(r.invalid) }), { duration: 10_000 });
      setPreview(null);
      setFile(null);
      await qc.invalidateQueries({ queryKey: ["device-punches"] });
    } catch (e) {
      notify.error(errorMessage(e));
    } finally {
      setBusy(false);
    }
  };
  const cols = (preview?.headers ?? []).map((h, i) => ({ value: String(i), label: h || `${i + 1}` }));
  return (
    <div className="max-w-3xl space-y-4">
      <p className="text-muted-foreground">{t("dev.import.help")}</p>
      <SelectField label={t("dev.import.device")} help={t("dev.import.deviceHelp")} value={device || fileDevices[0]?.id || ""} onChange={setDevice}
        options={fileDevices.map((d) => ({ value: d.id, label: d.name }))} />
      <input ref={input} type="file" accept=".xlsx,.csv,.txt" className="sr-only" tabIndex={-1} aria-hidden="true" onChange={(e) => pick(e.target.files?.[0])} data-testid="dev-file" />
      <Button variant="outline" className="h-12 md:h-11" disabled={busy || fileDevices.length === 0} onClick={() => input.current?.click()}><Upload aria-hidden="true" />{t("dev.import.pick")}</Button>
      {preview && (
        <div className="space-y-4" data-testid="dev-preview">
          <h2 className="font-bold">{t("dev.import.preview", { n: formatNumber(preview.totalRows) })}</h2>
          <div className="overflow-x-auto rounded-xl border bg-card">
            <table className="w-full text-sm">
              <thead className="border-b bg-muted/40"><tr>{preview.headers.map((h, i) => <th key={i} scope="col" className="px-3 py-2 text-start">{h}</th>)}</tr></thead>
              <tbody className="divide-y">{preview.rows.map((r, i) => <tr key={i}>{r.map((c, j) => <td key={j} className="px-3 py-1.5 whitespace-nowrap">{c}</td>)}</tr>)}</tbody>
            </table>
          </div>
          <div className="grid gap-4 sm:grid-cols-3">
            <SelectField label={t("dev.import.colPin")} help={t("dev.import.colPinHelp")} value={map.pin} onChange={(v) => setMap({ ...map, pin: v })} options={cols} />
            <SelectField label={t("dev.import.colDate")} help={t("dev.import.colDateHelp")} value={map.date} onChange={(v) => setMap({ ...map, date: v })} options={cols} />
            <SelectField label={t("dev.import.colTime")} help={t("dev.import.colTimeHelp")} value={map.time} onChange={(v) => setMap({ ...map, time: v })}
              options={[{ value: "", label: t("dev.import.none") }, ...cols]} />
          </div>
          <Toggle label={t("dev.import.header")} help={t("dev.import.headerHelp")} checked={map.header} onChange={(v) => setMap({ ...map, header: v })} />
          <Button className="h-12 md:h-11" disabled={busy} onClick={run} data-testid="dev-import-run">{t("dev.import.run")}</Button>
        </div>
      )}
    </div>
  );
}
