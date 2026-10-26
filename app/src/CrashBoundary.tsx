import { Component, type ErrorInfo, type ReactNode } from "react";

interface State {
  error: Error | null;
}

/** Crash screen: the web side of CrashForm.cs. Rust panics are logged by the panic hook. */
export class CrashBoundary extends Component<{ children: ReactNode }, State> {
  state: State = { error: null };

  static getDerivedStateFromError(error: Error): State {
    return { error };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error("Tech Bench crashed", error, info.componentStack);
  }

  render() {
    if (!this.state.error) return this.props.children;
    return (
      <div className="crash" role="alert">
        <h1>Tech Bench hit a problem</h1>
        <p>Nothing was sent to the engine. Restart the app to keep working.</p>
        <pre>{this.state.error.stack ?? String(this.state.error)}</pre>
        <button onClick={() => this.setState({ error: null })}>Try again</button>
      </div>
    );
  }
}
