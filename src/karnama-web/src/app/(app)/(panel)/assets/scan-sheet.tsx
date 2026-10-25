"use client";

import { EditorSheet } from "@/components/app/editor-sheet";
import { useI18n } from "@/lib/i18n/provider";
import { QrScanner } from "../attendance/qr-scanner";

/** Reads the QR label (…/assets?tag=KN-0001) or a typed property code. */
export function ScanSheet({ onClose, onTag }: { onClose: () => void; onTag: (tag: string) => void }) {
  const { t } = useI18n();
  const read = (code: string) => {
    const raw = code.trim();
    if (!raw) return;
    try {
      const u = new URL(raw);
      const tag = u.searchParams.get("tag");
      if (tag) return onTag(tag);
    } catch {
      /* not a URL: a typed code */
    }
    onTag(raw.slice(0, 30));
  };
  return (
    <EditorSheet title={t("as.scan")} description={t("as.scanHelp")} onClose={onClose} onSubmit={() => {}} busy={false} error={null} noSubmit testId="as-scan-sheet">
      <QrScanner onCode={read} />
    </EditorSheet>
  );
}
