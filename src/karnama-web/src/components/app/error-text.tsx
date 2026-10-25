"use client";

import { ApiError } from "@/lib/api";
import { useI18n } from "@/lib/i18n/provider";

/** Turns any error into a clear message (server messages are already translated) plus a trace code for support. */
export function useErrorMessage() {
  const { t } = useI18n();
  return (err: unknown) => {
    if (err instanceof ApiError) {
      if (err.isNetwork) return t("common.errorNetwork");
      return err.message || t("common.errorGeneric");
    }
    return t("common.errorGeneric");
  };
}

export function ErrorText({ error }: { error: unknown }) {
  const message = useErrorMessage();
  const { t } = useI18n();
  if (!error) return null;
  const trace = error instanceof ApiError && error.status >= 500 ? error.traceId : undefined;
  return (
    <p role="alert" className="text-sm text-destructive">
      {message(error)}
      {trace && <span className="mt-1 block text-xs opacity-80">{t("common.traceId", { id: trace.slice(0, 16) })}</span>}
    </p>
  );
}
