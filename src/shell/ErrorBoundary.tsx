import { Component, type ErrorInfo, type ReactNode } from "react";

import { EmptyState } from "./EmptyState";

interface ErrorBoundaryState {
  hasError: boolean;
  message?: string;
}

export class ErrorBoundary extends Component<{ children: ReactNode }, ErrorBoundaryState> {
  state: ErrorBoundaryState = { hasError: false };

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { hasError: true, message: error.message };
  }

  componentDidCatch(error: Error, info: ErrorInfo): void {
    console.error("Console error boundary caught", error, info);
  }

  handleReload = (): void => {
    window.location.reload();
  };

  render(): ReactNode {
    if (this.state.hasError) {
      return (
        <EmptyState
          tone="warning"
          title="Something went wrong"
          description={this.state.message ?? "Honua Console hit an unexpected error rendering this view."}
          primaryAction={
            <button type="button" className="hc-btn hc-btn--primary" onClick={this.handleReload}>
              Reload page
            </button>
          }
        />
      );
    }
    return this.props.children;
  }
}
