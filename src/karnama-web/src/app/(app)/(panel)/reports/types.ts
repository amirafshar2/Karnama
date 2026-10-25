export type ServiceCount = { serviceTypeId: string; name: string; count: number };
export type WorkRow = {
  userId: string; name: string; presenceMinutes: number; timerMinutes: number; usefulPercent: number | null; overtimeMinutes: number; lateMinutes: number; lateCount: number;
  unexcusedAbsences: number; excusedAbsences: number; leaveDays: number; doneCount: number; doneByService: ServiceCount[]; estimatedMinutes: number; actualMinutes: number;
  onTimePercent: number | null; firstTimePercent: number | null; revisions: number; botFiles: number; score: number | null;
};
export type DayPoint = { day: string; presenceMinutes: number; timerMinutes: number; done: number };
export type WorkReport = { from: string; to: string; rows: WorkRow[]; days: DayPoint[] };
export type WorkDetails = {
  shifts: { day: string; in: string; out: string | null; worked: number; late: number }[];
  timers: { start: string; stop: string | null; minutes: number; title: string; orderNumber: number }[];
  done: { id: string; title: string; orderNumber: number; service: string; estimated: number; actual: number; deadline: string | null; delivered: string | null; onTime: boolean | null; revisions: number; managerStars: number | null; clientStars: number | null }[];
};
export type Indicator = { key: string; score: number | null; value: string; weight: number; hint: string };
export type Score = { userId: string; name: string; total: number | null; enough: boolean; samples: number; indicators: Indicator[]; badges: string[] };
export type Scores = { from: string; to: string; minSamples: number; people: Score[]; rankingVisible: boolean };
export type Weights = { quality: number; speed: number; onTime: number; volume: number; discipline: number; manager: number; client: number };
export type ScoreSettings = { weights: Weights; minSamples: number; rankingForAll: boolean; serviceWeights: Record<string, number> | null };
export type TrendPoint = { label: string; from: string; total: number | null };
