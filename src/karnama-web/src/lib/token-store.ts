import { CLIENT_KIND } from "./config";

/**
 * Access token: memory only (never localStorage).
 * Refresh token: web = HttpOnly cookie handled by the browser; android = Keystore-backed secure storage (added in M7).
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
    return androidRefresh;
  },
  async setRefresh(token: string | null) {
    if (CLIENT_KIND === "web") return;
    // M7: replace with @capacitor-community/secure-storage (Android Keystore).
    androidRefresh = token;
  },
};

let androidRefresh: string | null = null;
