import type { Metadata } from "next";
import { SetupScreen } from "./setup-screen";

export const metadata: Metadata = { title: "راه‌اندازی" };

export default function SetupPage() {
  return <SetupScreen />;
}
