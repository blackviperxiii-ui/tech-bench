import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App";
import { CrashBoundary } from "./CrashBoundary";
import "./styles.css";

/** `?crash-test` opens the crash screen on purpose, so a test can check it. */
function CrashTest(): never {
  throw new Error("crash-test marker");
}

const crashTest = new URLSearchParams(window.location.search).has("crash-test");

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <CrashBoundary>{crashTest ? <CrashTest /> : <App />}</CrashBoundary>
  </StrictMode>,
);
