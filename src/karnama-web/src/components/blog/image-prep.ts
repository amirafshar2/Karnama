import { uploadForm } from "@/lib/api";

/**
 * Photos are shrunk in the browser before upload: WebP, at most 1600px wide, under 2MB. Phones send 5–10MB JPEGs;
 * the reader downloads ~150KB instead, and the server needs no image library.
 */
export async function prepareImage(file: File, maxWidth = 1600): Promise<{ blob: Blob; width: number; height: number }> {
  const bitmap = await createImageBitmap(file);
  const scale = Math.min(1, maxWidth / bitmap.width);
  const width = Math.round(bitmap.width * scale);
  const height = Math.round(bitmap.height * scale);
  const canvas = document.createElement("canvas");
  canvas.width = width;
  canvas.height = height;
  canvas.getContext("2d")!.drawImage(bitmap, 0, 0, width, height);
  bitmap.close();
  for (const q of [0.82, 0.7, 0.55, 0.4]) {
    const blob = await new Promise<Blob | null>((r) => canvas.toBlob(r, "image/webp", q));
    if (blob && blob.size < 1.9 * 1024 * 1024) return { blob, width, height };
  }
  throw new Error("too-large");
}

export async function uploadBlogImage(file: File, maxWidth?: number) {
  const { blob, width, height } = await prepareImage(file, maxWidth);
  const form = new FormData();
  form.append("file", blob, "image.webp");
  const r = await uploadForm<{ id: string; url: string }>("/api/v1/blog/media", form);
  return { ...r, width, height };
}
