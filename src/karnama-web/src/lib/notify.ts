"use client";

/**
 * Toasts without putting the toast library in every page's first load:
 * sonner is fetched the first time something is shown (bandwidth budget, R9).
 */
type Action = { label: string; onClick: () => void };
type Opts = { duration?: number; action?: Action };

const load = () => import("sonner").then((m) => m.toast);

export const notify = {
  message: (text: string, opts?: Opts) => void load().then((t) => t(text, opts)),
  success: (text: string, opts?: Opts) => void load().then((t) => t.success(text, opts)),
  info: (text: string, opts?: Opts) => void load().then((t) => t.info(text, opts)),
  error: (text: string, opts?: Opts) => void load().then((t) => t.error(text, opts)),
};
