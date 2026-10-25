"use client";

import { Loader2 } from "lucide-react";
import { ErrorText } from "@/components/app/error-text";
import { Button } from "@/components/ui/button";
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { useIsMobile } from "@/hooks/use-mobile";
import { useI18n } from "@/lib/i18n/provider";
import { cn } from "@/lib/utils";

/** Side sheet (bottom on phones) with a sticky save bar that never hides under the keyboard. */
export function EditorSheet({
  title,
  description,
  onClose,
  onSubmit,
  busy,
  error,
  loading,
  wide,
  children,
  testId,
}: {
  title: string;
  description?: string;
  onClose: () => void;
  onSubmit: () => void;
  busy: boolean;
  error: unknown;
  loading?: boolean;
  wide?: boolean;
  children: React.ReactNode;
  testId?: string;
}) {
  const { t, dir } = useI18n();
  const isMobile = useIsMobile();
  const side = isMobile ? "bottom" : dir === "rtl" ? "left" : "right";
  return (
    <Sheet open onOpenChange={(o) => !o && onClose()}>
      <SheetContent side={side} className={cn("flex flex-col gap-0 p-0", isMobile ? "h-[94dvh] rounded-t-2xl" : wide ? "w-full sm:max-w-3xl" : "w-full sm:max-w-xl")}>
        <SheetHeader className="border-b p-5">
          <SheetTitle className="text-xl">{title}</SheetTitle>
          {description && <SheetDescription>{description}</SheetDescription>}
        </SheetHeader>
        {loading ? (
          <div className="grid flex-1 place-items-center">
            <Loader2 className="animate-spin text-muted-foreground" aria-label={t("common.loading")} />
          </div>
        ) : (
          <form
            className="flex min-h-0 flex-1 flex-col"
            noValidate
            onSubmit={(e) => {
              e.preventDefault();
              onSubmit();
            }}
          >
            <div className="flex-1 space-y-6 overflow-y-auto p-5">{children}</div>
            <div className="safe-bottom sticky bottom-0 border-t bg-background p-4">
              <ErrorText error={error} />
              <div className="mt-2 flex gap-2">
                <Button type="submit" className="h-12 flex-1" disabled={busy} data-testid={testId ? `${testId}-save` : undefined}>
                  {busy && <Loader2 className="animate-spin" aria-hidden="true" />}
                  {busy ? t("common.saving") : t("common.save")}
                </Button>
                <Button type="button" variant="outline" className="h-12" onClick={onClose}>
                  {t("common.cancel")}
                </Button>
              </div>
            </div>
          </form>
        )}
      </SheetContent>
    </Sheet>
  );
}
