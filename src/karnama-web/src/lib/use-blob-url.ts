"use client";

import { useEffect, useState } from "react";
import { apiBlob } from "@/lib/api";

/** Private files come through the API with the token; a blob URL is made only when needed. */
export function useBlobUrl(path: string, enabled: boolean) {
  const [url, setUrl] = useState<string | null>(null);
  useEffect(() => {
    if (!enabled) return;
    let made: string | null = null;
    let gone = false;
    apiBlob(path).then((b) => {
      if (gone) return;
      made = URL.createObjectURL(b);
      setUrl(made);
    }).catch(() => undefined);
    return () => {
      gone = true;
      if (made) URL.revokeObjectURL(made);
    };
  }, [path, enabled]);
  return url;
}
