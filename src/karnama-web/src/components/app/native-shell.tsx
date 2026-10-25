"use client";

import { useRouter } from "next/navigation";
import { useEffect } from "react";
import { initNative } from "@/lib/native";

export function NativeShell() {
  const router = useRouter();
  useEffect(() => {
    let dispose: (() => void) | undefined;
    void initNative({ back: () => router.back(), push: (h) => router.push(h) }).then((d) => (dispose = d));
    return () => dispose?.();
  }, [router]);
  return null;
}
