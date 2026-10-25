export type AssetStatus = "available" | "assigned" | "inRepair" | "lost" | "retired";
export type AssignmentState = "pendingAcceptance" | "active" | "returnSubmitted" | "returned" | "transferred" | "cancelled";
export type Category = { id: string; name: string; sortOrder: number; isActive: boolean; count: number };
export type AssetItem = {
  id: string; name: string; categoryId: string; categoryName: string; brand: string | null; model: string | null; serial: string | null; tag: string;
  status: AssetStatus; holderId: string | null; holderName: string | null; assignmentState: AssignmentState | null; expectedReturn: string | null; overdue: boolean;
  warrantyUntil: string | null; coverPhotoId: string | null; version: number;
};
export type Assignment = {
  id: string; userId: string; userName: string; handedByName: string; handedAt: string; expectedReturn: string | null; conditionOut: string | null;
  accessories: string[]; acceptedAt: string | null; state: AssignmentState; returnedAt: string | null; conditionIn: string | null; damage: string | null;
  transferToId: string | null; transferToName: string | null; version: number;
};
export type AssetEvent = { id: number; kind: string; actorName: string | null; userName: string | null; note: string | null; at: string };
export type Photo = { id: string; stage: "profile" | "handOver" | "return"; assignmentId: string | null; at: string };
export type AssetDetail = {
  asset: AssetItem; purchasedOn: string | null; purchasePrice: number | null; note: string | null; current: Assignment | null; past: Assignment[];
  history: AssetEvent[]; photos: Photo[];
  can: { manage: boolean; assign: boolean; accept: boolean; submitReturn: boolean; transfer: boolean; acceptTransfer: boolean };
};
export type Person = { id: string; name: string };
export const STATUSES: AssetStatus[] = ["available", "assigned", "inRepair", "lost", "retired"];
