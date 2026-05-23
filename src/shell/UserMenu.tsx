import { useEffect, useId, useRef, useState } from "react";

import { useSession } from "../auth/SessionContext";

import "./shell.css";

export function UserMenu(): JSX.Element | null {
  const { session, signIn, signOut } = useSession();
  const [open, setOpen] = useState(false);
  const triggerId = useId();
  const menuId = useId();
  const containerRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    if (!open) return;
    function onDocClick(event: MouseEvent): void {
      if (!containerRef.current?.contains(event.target as Node)) {
        setOpen(false);
      }
    }
    function onKey(event: KeyboardEvent): void {
      if (event.key === "Escape") setOpen(false);
    }
    document.addEventListener("mousedown", onDocClick);
    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("mousedown", onDocClick);
      document.removeEventListener("keydown", onKey);
    };
  }, [open]);

  if (session.status === "loading") {
    return <span className="hc-usermenu__placeholder" aria-hidden="true" />;
  }

  if (session.status !== "authenticated") {
    return (
      <button
        type="button"
        className="hc-btn hc-btn--ghost"
        onClick={() => {
          void signIn();
        }}
        data-testid="signin-trigger"
      >
        Sign in
      </button>
    );
  }

  const initials = session.user.displayName
    .split(/\s+/)
    .map((part) => part.charAt(0))
    .join("")
    .slice(0, 2)
    .toUpperCase();

  return (
    <div className="hc-usermenu" ref={containerRef}>
      <button
        type="button"
        id={triggerId}
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={menuId}
        className="hc-usermenu__trigger"
        onClick={() => setOpen((prev) => !prev)}
        data-testid="usermenu-trigger"
      >
        <span className="hc-usermenu__avatar" aria-hidden="true">
          {initials || "?"}
        </span>
        <span className="hc-usermenu__name">{session.user.displayName}</span>
      </button>
      {open && (
        <div role="menu" id={menuId} aria-labelledby={triggerId} className="hc-usermenu__panel">
          <div className="hc-usermenu__identity">
            <strong>{session.user.displayName}</strong>
            <span className="hc-usermenu__email">{session.user.email}</span>
            <span className="hc-usermenu__workspace">{session.workspace.name}</span>
          </div>
          <div className="hc-usermenu__group" role="presentation">
            <button
              type="button"
              role="menuitem"
              className="hc-usermenu__item hc-usermenu__item--button"
              onClick={() => {
                void signOut();
              }}
              data-testid="usermenu-signout"
            >
              Sign out
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
