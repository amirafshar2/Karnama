export type WorkStatus = "waiting" | "todo" | "inProgress" | "changesRequested" | "inReview" | "clientReview" | "done" | "cancelled";
export type Priority = "normal" | "high" | "urgent";

export type WorkCard = {
  id: string;
  orderId: string;
  orderNumber: number;
  title: string;
  clientId: string;
  clientName: string;
  serviceName: string;
  stageName: string;
  stageOrder: number;
  stageCount: number;
  status: WorkStatus;
  priority: Priority;
  assigneeId: string | null;
  assigneeName: string | null;
  deadline: string | null;
  estimatedMinutes: number;
  revisionCount: number;
  comments: number;
  files: number;
  rank: number;
  version: number;
};

export type FileItem = { id: string; fileName: string; contentType: string; size: number; kind: string | null; purpose: string; clientVisible: boolean; fromClient: boolean; workItemId: string | null; createdAt: string };

export type OrderDetail = {
  id: string;
  number: number;
  title: string;
  note: string | null;
  clientId: string;
  clientName: string;
  serviceTypeId: string;
  serviceName: string;
  status: "submitted" | "inProgress" | "completed" | "rejected" | "cancelled";
  priority: Priority;
  source: "admin" | "client";
  deadline: string | null;
  outsidePackage: boolean;
  price: number | null;
  rejectReason: string | null;
  answers: { key: string; label: string; type: string; value: unknown }[];
  stages: { id: string; order: number; name: string; status: WorkStatus; assigneeId: string | null; assigneeName: string | null; deadline: string | null }[];
  files: FileItem[];
  createdAt: string;
  version: number;
};

export type Comment = { id: string; authorName: string; fromClient: boolean; visibility: "internal" | "client"; body: string; createdAt: string; mine: boolean };

export type WorkDetail = { card: WorkCard; order: OrderDetail; comments: Comment[]; files: FileItem[]; allowedMoves: WorkStatus[]; canAssign: boolean; canTalkToClient: boolean; managerStars: number | null; clientStars: number | null; canRate: boolean };

/** Board columns, in workflow order. */
export const COLUMNS: WorkStatus[] = ["waiting", "todo", "inProgress", "changesRequested", "inReview", "clientReview", "done"];
