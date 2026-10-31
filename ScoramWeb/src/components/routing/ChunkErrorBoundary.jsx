import { Component } from "react";
import { isChunkLoadError } from "./lazyWithRetry";

// Catches a lazily-loaded route failing to load (offline, CDN hiccup, or a deployment that replaced
// the chunk -- see lazyWithRetry for the automatic one-time reload) and any render error beneath it,
// and shows a retry instead of a blank screen. `resetKey` (the current pathname) clears the error
// when the person navigates somewhere else, so one broken page never traps the rest of the app.
export default class ChunkErrorBoundary extends Component {
  state = { error: null, resetKey: this.props.resetKey };

  static getDerivedStateFromError(error) {
    return { error };
  }

  // Navigating to a different route (resetKey changes) clears a previous error.
  static getDerivedStateFromProps(props, state) {
    return props.resetKey !== state.resetKey ? { error: null, resetKey: props.resetKey } : null;
  }

  componentDidCatch(error) {
    // eslint-disable-next-line no-console
    console.error("Route failed to render:", error);
  }

  render() {
    const { error } = this.state;
    if (!error) return this.props.children;

    const chunkProblem = isChunkLoadError(error);
    return (
      <div role="alert" className="flex min-h-[60vh] flex-col items-center justify-center gap-3 px-6 text-center">
        <p className="text-base font-bold text-ink-900">
          {chunkProblem ? "Couldn't load this page" : "Something went wrong"}
        </p>
        <p className="max-w-sm text-sm text-ink-400">
          {chunkProblem
            ? "Scoram may have just been updated, or your connection dropped. Please try again."
            : "Please reload the page. If it keeps happening, let us know."}
        </p>
        <button
          type="button"
          onClick={() => window.location.reload()}
          className="rounded-xl2 bg-primary-600 px-4 py-2 text-sm font-semibold text-white hover:bg-primary-700"
        >
          Reload
        </button>
      </div>
    );
  }
}
