import type { Metadata } from "next";
import { SetupScreen } from "./setup-screen";

export const metadata: Metadata = { title: "Einrichtung" };

export default function SetupPage() {
  return <SetupScreen />;
}
