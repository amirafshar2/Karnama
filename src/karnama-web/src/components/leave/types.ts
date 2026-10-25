export type LeaveType = { id: string; name: string; kind: string; paid: boolean; needsDocument: boolean; annualDays: number | null; color: string; isActive: boolean; sortOrder: number };
export type LeaveRequest = { id: string; userId: string; userName: string; leaveTypeId: string; typeName: string; color: string; hourly: boolean; from: string; to: string; minutes: number; reason: string; status: "pending" | "approved" | "rejected" | "cancelled"; decisionNote: string | null; byManager: boolean; createdAt: string; missingDocument: boolean };
export type Balance = { leaveTypeId: string; typeName: string; color: string; allowanceDays: number | null; usedDays: number; leftDays: number | null };
export type Absence = { id: string; userId: string; userName: string; day: string; expectedAt: string; reasonDeadline: string; status: "detected" | "reasonSubmitted" | "excused" | "unexcused" | "becameLate"; reason: string | null; actualFrom: string | null; actualTo: string | null; lateMinutes: number; autoDecided: boolean; decisionNote: string | null };
/** "1405/08/03" date (ISO from the Jalali field) + "09:30" → ISO with Tehran offset. */
export const at = (dayIso: string | null, hhmm: string) => (dayIso && /^\d{1,2}:\d{2}$/.test(hhmm) ? `${dayIso.slice(0, 10)}T${hhmm.padStart(5, "0")}:00+03:30` : null);
export const addDays = (dayIso: string, n: number) => {
  const d = new Date(`${dayIso.slice(0, 10)}T12:00:00Z`);
  d.setUTCDate(d.getUTCDate() + n);
  return d.toISOString().slice(0, 10);
};
