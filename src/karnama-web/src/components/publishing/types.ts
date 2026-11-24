import type { FileItem } from "@/lib/work-types";

export type PublishStatus = "scheduled" | "due" | "published" | "overdue" | "cancelled";
export type PublishJob = {
  id: string;
  clientId: string;
  clientName: string;
  orderId: string | null;
  orderNumber: number | null;
  platformId: string;
  platformName: string;
  handle: string;
  management: "agencyManaged" | "clientManaged";
  publishAt: string;
  caption: string;
  hashtags: string[];
  files: FileItem[];
  status: PublishStatus;
  publishedAt: string | null;
  postUrl: string | null;
  publishedByClient: boolean;
  version: number;
};

import { atBerlin, splitBerlin } from "@/lib/berlin";

/** Agency local time is Europe/Berlin (names kept from the first version). */
export const atTehran = atBerlin;
export const splitTehran = splitBerlin;

export const statusTone: Record<PublishStatus, string> = {
  scheduled: "border-border bg-card",
  due: "border-amber-500 bg-amber-50 dark:bg-amber-950/40",
  overdue: "border-destructive bg-destructive/10",
  published: "border-emerald-600/60 bg-emerald-50 dark:bg-emerald-950/30",
  cancelled: "border-dashed opacity-60",
};
