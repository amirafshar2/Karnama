export type Camera = { id: string; name: string; location: string | null; sortOrder: number; isActive: boolean; online: boolean; lastOnlineAt: string | null;
  hasSubstream: boolean; mainMasked: string | null; subMasked: string | null; version: number };
export type CctvStatus = { configured: boolean; cameras: Camera[] };
export type Live = { whepUrl: string; token: string; expiresAt: string; sessionId: string; idleSeconds: number };
