import { CLIENT_KIND } from "./config";
import { secureGet, secureSet } from "./native";

/**
 * Access token: memory only (never localStorage).
 * Refresh token: web = HttpOnly cookie handled by the browser; android = Keystore-backed secure storage.
 */
let accessToken: string | null = null;

export const tokenStore = {
  get access() {
    return accessToken;
  },
  setAccess(token: string | null) {
    accessToken = token;
  },
  async getRefresh(): Promise<string | null> {
    if (CLIENT_KIND === "web") return null;
    return secureGet(REFRESH_KEY);
  },
  async setRefresh(token: string | null) {
    if (CLIENT_KIND === "web") return;
    await secureSet(REFRESH_KEY, token);
  },
};

const REFRESH_KEY = "karnama.refresh";
