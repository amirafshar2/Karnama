"use client";

import dynamic from "next/dynamic";
import { useSearchParams } from "next/navigation";
import { Suspense } from "react";
import { useRequire } from "@/components/app/use-require";
import { faBlog } from "@/lib/i18n/fa-blog";
import { registerTexts } from "@/lib/i18n/registry";

registerTexts(faBlog);

// The editor (Tiptap/ProseMirror) is big; only this page downloads it, and only after the shell is up.
const PostEditor = dynamic(() => import("@/components/blog/post-editor").then((m) => m.PostEditor), {
  ssr: false,
  loading: () => <div className="h-96 animate-pulse rounded-xl bg-muted" />,
});

function Inner() {
  const id = useSearchParams().get("id");
  const allowed = useRequire([{ code: "blog.write", scope: "own" }, { code: "blog.publish", scope: "all" }]);
  if (!allowed || !id || !/^[0-9a-f-]{36}$/i.test(id)) return null;
  return <PostEditor id={id} />;
}

export default function EditPostPage() {
  return (
    <Suspense>
      <Inner />
    </Suspense>
  );
}
