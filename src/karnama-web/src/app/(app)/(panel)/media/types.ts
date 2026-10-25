export type Media = {
  id: string;
  kind: "video" | "photo" | "document" | "audio" | "voice" | "animation" | "videoNote";
  senderId: string;
  senderName: string;
  fileSize: number | null;
  durationSeconds: number | null;
  fileName: string | null;
  caption: string | null;
  sentAt: string;
  receivedAt: string;
  workItemId: string | null;
  workTitle: string | null;
  orderNumber: number | null;
  clientName: string | null;
  status: "received" | "archived" | "sentToAdmins" | "assigned" | "delivered" | "failed";
  assignedToId: string | null;
  assignedToName: string | null;
  deliveredAt: string | null;
  error: string | null;
  hasThumb: boolean;
  archived: boolean;
};

export type Targets = {
  staff: { id: string; name: string; linked: boolean }[];
  work: { id: string; title: string; orderNumber: number; clientName: string; assigneeId: string | null; deadline: string | null; clientLinked: boolean }[];
};
