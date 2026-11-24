/** Germany: Europe/Berlin with summer time. Helpers to build ISO strings in local agency time. */
const TZ = "Europe/Berlin";

/** UTC offset of Berlin on that day/time, e.g. "+01:00" in winter, "+02:00" in summer. */
export function berlinOffset(day: string, time = "12:00"): string {
  const probe = new Date(`${day.slice(0, 10)}T${time}:00Z`);
  if (Number.isNaN(probe.getTime())) return "+01:00";
  const name = new Intl.DateTimeFormat("en-US", { timeZone: TZ, timeZoneName: "longOffset" }).formatToParts(probe).find((p) => p.type === "timeZoneName")?.value ?? "GMT+01:00";
  return name === "GMT" ? "+00:00" : name.replace("GMT", "");
}

/** "2026-10-25" + "18:30" → ISO in Berlin time. */
export const atBerlin = (day: string, time: string) => `${day.slice(0, 10)}T${time}:00${berlinOffset(day, time)}`;

/** ISO → {day:"2026-10-25", time:"18:30"} in Berlin time. */
export function splitBerlin(iso: string) {
  const parts = new Intl.DateTimeFormat("en-CA", { timeZone: TZ, year: "numeric", month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit", hourCycle: "h23" }).formatToParts(new Date(iso));
  const v = (t: Intl.DateTimeFormatPartTypes) => parts.find((p) => p.type === t)?.value ?? "00";
  return { day: `${v("year")}-${v("month")}-${v("day")}`, time: `${v("hour")}:${v("minute")}` };
}
