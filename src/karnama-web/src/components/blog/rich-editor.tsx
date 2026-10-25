"use client";

/**
 * ⚠ TEMPORARY STAND-IN — 21st.dev key unavailable (docs/21st-components.md): Tiptap (MIT) with our own toolbar.
 * Blocks: h2/h3, bold/italic/link, lists, quote, image with alt + caption, table, tip box, call-to-action button, divider.
 * Markdown shortcuts as you type ("## ", "- ", "> ", "**bold**"); pasted Markdown is converted on the server.
 */
import { mergeAttributes, Node, type Editor } from "@tiptap/core";
import Image from "@tiptap/extension-image";
import { TableKit } from "@tiptap/extension-table";
import { Placeholder } from "@tiptap/extensions";
import { EditorContent, useEditor, useEditorState } from "@tiptap/react";
import StarterKit from "@tiptap/starter-kit";
import {
  Bold, Heading2, Heading3, ImagePlus, Italic, Lightbulb, Link2, List, ListOrdered, Loader2, Megaphone, Minus, Quote, Redo2, Table as TableIcon, Undo2,
} from "lucide-react";
import { useRef, useState } from "react";
import { useErrorMessage } from "@/components/app/error-text";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import { useI18n } from "@/lib/i18n/provider";
import type { TextKey } from "@/lib/i18n/registry";
import { notify } from "@/lib/notify";
import { cn } from "@/lib/utils";
import { uploadBlogImage } from "./image-prep";

const Callout = Node.create({
  name: "callout",
  group: "block",
  content: "paragraph+",
  defining: true,
  addAttributes: () => ({ tone: { default: "tip", parseHTML: (el) => el.getAttribute("data-tone") ?? "tip", renderHTML: (a) => ({ "data-tone": a.tone }) } }),
  parseHTML: () => [{ tag: "aside.callout" }],
  renderHTML: ({ HTMLAttributes }) => ["aside", mergeAttributes(HTMLAttributes, { class: "callout" }), 0],
});

const CtaButton = Node.create({
  name: "ctaButton",
  group: "block",
  atom: true,
  addAttributes: () => ({ href: { default: "/" }, label: { default: "" } }),
  parseHTML: () => [{ tag: "p.cta", getAttrs: (el) => ({ href: el.querySelector("a")?.getAttribute("href") ?? "/", label: el.textContent ?? "" }) }],
  renderHTML: ({ node }) => ["p", { class: "cta" }, ["span", { class: "cta-button", "data-href": node.attrs.href }, node.attrs.label]],
});

const looksLikeMarkdown = (s: string) => /^(#{1,6}\s|[-*+]\s|\d+\.\s|>\s|```)|\*\*[^*\n]+\*\*|\[[^\]\n]+\]\([^)\n]+\)/m.test(s);

export function useRichEditor(initialJson: string, onChange: () => void, placeholder: string) {
  return useEditor({
    immediatelyRender: false,
    extensions: [
      StarterKit.configure({
        heading: { levels: [2, 3] },
        link: { openOnClick: false, autolink: true, HTMLAttributes: { rel: null, target: null } },
      }),
      Image.configure({ inline: false, allowBase64: false }),
      TableKit.configure({ table: { resizable: false } }),
      Placeholder.configure({ placeholder }),
      Callout,
      CtaButton,
    ],
    content: (() => {
      try {
        return JSON.parse(initialJson);
      } catch {
        return { type: "doc", content: [{ type: "paragraph" }] };
      }
    })(),
    editorProps: {
      attributes: { class: "prose-fa editor-body min-h-[50vh] max-w-none outline-none", "data-testid": "editor-body" },
      handlePaste: (view, event) => {
        const text = event.clipboardData?.getData("text/plain") ?? "";
        const html = event.clipboardData?.getData("text/html") ?? "";
        if (html || !looksLikeMarkdown(text)) return false;
        event.preventDefault();
        void api<{ html: string }>("/api/v1/blog/markdown", { method: "POST", body: { markdown: text } })
          .then((r) => editorRef.current?.chain().focus().insertContent(r.html).run())
          .catch(() => editorRef.current?.chain().focus().insertContent(text).run());
        return true;
      },
    },
    onCreate: ({ editor }) => {
      editorRef.current = editor;
    },
    onUpdate: () => onChange(),
  });
}
// Module-level handle for the paste handler (one editor per page).
const editorRef: { current: Editor | null } = { current: null };

function Tool({ label, active, onClick, children, testId }: { label: string; active?: boolean; onClick: () => void; children: React.ReactNode; testId?: string }) {
  return (
    <button
      type="button"
      aria-label={label}
      title={label}
      aria-pressed={active}
      onMouseDown={(e) => e.preventDefault()}
      onClick={onClick}
      className={cn("grid size-10 shrink-0 place-items-center rounded-md hover:bg-muted", active && "bg-foreground text-background hover:bg-foreground")}
      data-testid={testId}
    >
      {children}
    </button>
  );
}

export function EditorToolbar({ editor }: { editor: Editor }) {
  const { t } = useI18n();
  const errorMessage = useErrorMessage();
  const file = useRef<HTMLInputElement>(null);
  const [panel, setPanel] = useState<"link" | "cta" | null>(null);
  const [href, setHref] = useState("");
  const [label, setLabel] = useState("");
  const [uploading, setUploading] = useState(false);
  const s = useEditorState({
    editor,
    selector: ({ editor: e }) => ({
      h2: e.isActive("heading", { level: 2 }), h3: e.isActive("heading", { level: 3 }), bold: e.isActive("bold"), italic: e.isActive("italic"),
      link: e.isActive("link"), bullet: e.isActive("bulletList"), ordered: e.isActive("orderedList"), quote: e.isActive("blockquote"),
      callout: e.isActive("callout"), table: e.isActive("table"), undo: e.can().undo(), redo: e.can().redo(),
    }),
  });
  const c = () => editor.chain().focus();
  const L = (k: TextKey) => t(k);

  const pickImage = async (f: File | undefined) => {
    if (!f) return;
    setUploading(true);
    try {
      const img = await uploadBlogImage(f);
      c().setImage({ src: img.url, alt: "", width: img.width, height: img.height } as never).run();
    } catch (e) {
      notify.error(errorMessage(e));
    } finally {
      setUploading(false);
      if (file.current) file.current.value = "";
    }
  };

  return (
    <div className="sticky top-14 z-10 -mx-1 space-y-2 border-b bg-background/95 py-2 backdrop-blur" data-testid="editor-toolbar">
      <div className="flex flex-wrap items-center gap-0.5" role="toolbar" aria-label={t("seo.title")}>
        <Tool label={L("toolbar.h2")} active={s.h2} onClick={() => c().toggleHeading({ level: 2 }).run()} testId="tb-h2"><Heading2 className="size-5" /></Tool>
        <Tool label={L("toolbar.h3")} active={s.h3} onClick={() => c().toggleHeading({ level: 3 }).run()}><Heading3 className="size-5" /></Tool>
        <span className="mx-1 h-6 w-px bg-border" aria-hidden="true" />
        <Tool label={L("toolbar.bold")} active={s.bold} onClick={() => c().toggleBold().run()}><Bold className="size-5" /></Tool>
        <Tool label={L("toolbar.italic")} active={s.italic} onClick={() => c().toggleItalic().run()}><Italic className="size-5" /></Tool>
        <Tool
          label={L("toolbar.link")}
          active={s.link || panel === "link"}
          onClick={() => {
            setHref((editor.getAttributes("link").href as string) ?? "");
            setPanel(panel === "link" ? null : "link");
          }}
          testId="tb-link"
        >
          <Link2 className="size-5" />
        </Tool>
        <span className="mx-1 h-6 w-px bg-border" aria-hidden="true" />
        <Tool label={L("toolbar.bullet")} active={s.bullet} onClick={() => c().toggleBulletList().run()}><List className="size-5" /></Tool>
        <Tool label={L("toolbar.ordered")} active={s.ordered} onClick={() => c().toggleOrderedList().run()}><ListOrdered className="size-5" /></Tool>
        <Tool label={L("toolbar.quote")} active={s.quote} onClick={() => c().toggleBlockquote().run()}><Quote className="size-5" /></Tool>
        <Tool label={L("toolbar.callout")} active={s.callout} onClick={() => c().toggleWrap("callout").run()} testId="tb-callout"><Lightbulb className="size-5" /></Tool>
        <span className="mx-1 h-6 w-px bg-border" aria-hidden="true" />
        <Tool label={L("toolbar.image")} onClick={() => file.current?.click()} testId="tb-image">
          {uploading ? <Loader2 className="size-5 animate-spin" /> : <ImagePlus className="size-5" />}
        </Tool>
        <Tool label={L("toolbar.table")} active={s.table} onClick={() => c().insertTable({ rows: 3, cols: 3, withHeaderRow: true }).run()}><TableIcon className="size-5" /></Tool>
        <Tool label={L("toolbar.cta")} active={panel === "cta"} onClick={() => setPanel(panel === "cta" ? null : "cta")}><Megaphone className="size-5" /></Tool>
        <Tool label={L("toolbar.hr")} onClick={() => c().setHorizontalRule().run()}><Minus className="size-5" /></Tool>
        <span className="mx-1 h-6 w-px bg-border" aria-hidden="true" />
        <Tool label={L("toolbar.undo")} onClick={() => s.undo && c().undo().run()}><Undo2 className="size-5 rtl:-scale-x-100" /></Tool>
        <Tool label={L("toolbar.redo")} onClick={() => s.redo && c().redo().run()}><Redo2 className="size-5 rtl:-scale-x-100" /></Tool>
        <input ref={file} type="file" accept="image/png,image/jpeg,image/webp" className="hidden" onChange={(e) => void pickImage(e.target.files?.[0])} data-testid="tb-image-input" />
      </div>
      {s.table && (
        <div className="flex flex-wrap gap-1 text-xs">
          <Button type="button" size="sm" variant="outline" onClick={() => c().addRowAfter().run()}>+ {t("toolbar.tableRow")}</Button>
          <Button type="button" size="sm" variant="outline" onClick={() => c().addColumnAfter().run()}>+ {t("toolbar.tableCol")}</Button>
          <Button type="button" size="sm" variant="outline" onClick={() => c().deleteRow().run()}>− {t("toolbar.tableRow")}</Button>
          <Button type="button" size="sm" variant="outline" onClick={() => c().deleteColumn().run()}>− {t("toolbar.tableCol")}</Button>
          <Button type="button" size="sm" variant="ghost" className="text-destructive" onClick={() => c().deleteTable().run()}>{t("toolbar.tableDelete")}</Button>
        </div>
      )}
      {panel === "link" && (
        <form
          className="flex flex-wrap items-center gap-2"
          onSubmit={(e) => {
            e.preventDefault();
            if (href.trim()) c().extendMarkRange("link").setLink({ href: href.trim() }).run();
            setPanel(null);
          }}
        >
          <input autoFocus dir="ltr" value={href} onChange={(e) => setHref(e.target.value)} placeholder={t("toolbar.linkUrl")} aria-label={t("toolbar.linkUrl")} className="h-10 min-w-0 flex-1 rounded-md border bg-transparent px-3 text-sm" data-testid="tb-link-url" />
          <Button type="submit" size="sm" className="h-10">{t("toolbar.apply")}</Button>
          {s.link && (
            <Button type="button" size="sm" variant="ghost" className="h-10" onClick={() => { c().unsetLink().run(); setPanel(null); }}>
              {t("toolbar.unlink")}
            </Button>
          )}
        </form>
      )}
      {panel === "cta" && (
        <form
          className="flex flex-wrap items-center gap-2"
          onSubmit={(e) => {
            e.preventDefault();
            if (label.trim() && href.trim()) c().insertContent({ type: "ctaButton", attrs: { href: href.trim(), label: label.trim() } }).run();
            setPanel(null);
          }}
        >
          <input autoFocus value={label} onChange={(e) => setLabel(e.target.value)} placeholder={t("toolbar.ctaLabel")} aria-label={t("toolbar.ctaLabel")} className="h-10 min-w-0 flex-1 rounded-md border bg-transparent px-3 text-sm" />
          <input dir="ltr" value={href} onChange={(e) => setHref(e.target.value)} placeholder={t("toolbar.ctaHref")} aria-label={t("toolbar.ctaHref")} className="h-10 min-w-0 flex-1 rounded-md border bg-transparent px-3 text-sm" />
          <Button type="submit" size="sm" className="h-10">{t("toolbar.apply")}</Button>
        </form>
      )}
    </div>
  );
}

/** Alt text and caption of the selected image (alt matters for Google and screen readers). */
export function ImageSettings({ editor }: { editor: Editor }) {
  const { t } = useI18n();
  const img = useEditorState({ editor, selector: ({ editor: e }) => (e.isActive("image") ? (e.getAttributes("image") as { alt?: string; title?: string; src: string }) : null) });
  if (!img) return null;
  const set = (attrs: Record<string, string>) => editor.chain().updateAttributes("image", attrs).run();
  return (
    <section className="space-y-3 rounded-xl border-2 border-foreground/20 bg-card p-4" data-testid="image-settings">
      <h3 className="font-bold">{t("image.selected")}</h3>
      <label className="block space-y-1 text-sm">
        <span className="font-medium">{t("image.alt")}</span>
        <input value={img.alt ?? ""} onChange={(e) => set({ alt: e.target.value })} className="h-11 w-full rounded-md border bg-transparent px-3 text-base" data-testid="image-alt" />
        <span className="block text-xs text-muted-foreground">{t("image.altHelp")}</span>
      </label>
      <label className="block space-y-1 text-sm">
        <span className="font-medium">{t("image.caption")}</span>
        <input value={img.title ?? ""} onChange={(e) => set({ title: e.target.value })} className="h-11 w-full rounded-md border bg-transparent px-3 text-base" />
      </label>
    </section>
  );
}

export { EditorContent };
