import { Component, type ErrorInfo, type ReactNode } from "react";
import { crashOpenFolder, crashReport, type CrashReport } from "./api";

interface State {
  error: Error | null;
  report: CrashReport | null;
  status: string;
}

/** What goes in the log and on the clipboard: the error, its stack and the React component path. */
function details(error: Error, componentStack?: string | null): string {
  const stack = error.stack ?? String(error);
  const head = stack.startsWith(String(error)) ? stack : `${String(error)}\n${stack}`;
  return componentStack ? `${head}\nComponent stack:${componentStack}` : head;
}

/** Crash screen: the web side of CrashForm.cs. Rust panics are logged by the panic hook. */
export class CrashBoundary extends Component<{ children: ReactNode }, State> {
  state: State = { error: null, report: null, status: "" };

  static getDerivedStateFromError(error: Error): Partial<State> {
    return { error, report: null, status: "" };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error("Tech Bench crashed", error, info.componentStack);
    const text = details(error, info.componentStack);
    crashReport("UI", text)
      .then((report) => this.setState({ report }))
      .catch(() =>
        // No backend (plain vite dev): the text alone, without a Log line.
        this.setState({ report: { logPath: "", clipboard: `Tech Bench crash\nWhere: UI\n\n${text}\n` } }),
      );
  }

  private copy = () => {
    const text = this.state.report?.clipboard;
    if (!text) return;
    navigator.clipboard
      .writeText(text)
      .then(() => this.setState({ status: "Details copied." }))
      .catch((e: unknown) => this.setState({ status: `Could not copy the details: ${String(e)}` }));
  };

  private openLog = () => {
    crashOpenFolder()
      .then(() => this.setState({ status: "" }))
      .catch((e: unknown) => this.setState({ status: String(e) }));
  };

  render() {
    const { error, report, status } = this.state;
    if (!error) return this.props.children;
    return (
      <div className="crash" role="alert">
        <h1>Tech Bench hit a problem</h1>
        <p>Nothing was sent to the engine. Restart the app to keep working.</p>
        {report?.logPath && <p className="mini">Details were saved to {report.logPath}</p>}
        <pre>{error.stack ?? String(error)}</pre>
        <div className="bar">
          <button onClick={() => this.setState({ error: null, report: null, status: "" })}>Try again</button>
          <button onClick={this.copy} disabled={!report}>
            Copy details
          </button>
          {report?.logPath && <button onClick={this.openLog}>Open log folder</button>}
        </div>
        {status && (
          <p className="mini" role="status">
            {status}
          </p>
        )}
      </div>
    );
  }
}
