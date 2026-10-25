import { Providers } from "../providers";

/** The app (panel, login, setup): auth, data, i18n and live updates. The public site does without all of it. */
export default function AppLayout({ children }: { children: React.ReactNode }) {
  return <Providers>{children}</Providers>;
}
