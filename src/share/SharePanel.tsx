import { useEffect, useMemo, useState } from "react";

import type { Session } from "../auth/types.js";
import type { ContentItem, GetDependenciesResponse } from "../contracts/content-item.js";
import { CopyRow } from "../ui/CopyRow.js";
import { Pill } from "../ui/Pill.js";
import { VisibilityPill, visibilityLabel } from "../ui/VisibilityPill.js";
import { classifyPatchResult } from "./client.js";
import {
  type InviteRole,
  ROLE_MATRIX,
  canInviteRole,
  canUpdateSharing,
  resolvePortalItemRole,
  roleMatrixEntry,
} from "./rbac.js";
import { type ShareReviewBlocker, reviewShareChange } from "./review.js";
import type { PatchAccessResult, ShareAccess, SharingTier } from "./types.js";

interface SharePanelProps {
  readonly item: ContentItem;
  readonly session: Session;
  readonly loadClosure: () => Promise<GetDependenciesResponse>;
  readonly patchAccess: (access: ShareAccess) => Promise<PatchAccessResult>;
  readonly onAccessChange: (access: ShareAccess) => void;
}

type ShareStatus =
  | { kind: "idle" }
  | { kind: "saving" }
  | { kind: "ok"; access: ShareAccess }
  | { kind: "blocked"; blockers: readonly ShareReviewBlocker[] }
  | { kind: "unauthorized" }
  | { kind: "error"; message: string };

const SHARING_OPTIONS: readonly { value: SharingTier; label: string }[] = [
  { value: "private", label: "Private" },
  { value: "org", label: "Organization" },
  { value: "group", label: "Group" },
  { value: "public-link", label: "Public link" },
  { value: "public", label: "Public" },
];

export function SharePanel({ item, session, loadClosure, patchAccess, onAccessChange }: SharePanelProps): JSX.Element {
  const role = resolvePortalItemRole(session, item);
  const roleEntry = roleMatrixEntry(role);
  const editable = canUpdateSharing(role);
  const [access, setAccess] = useState<ShareAccess>(() => itemToShareAccess(item));
  const [groupId, setGroupId] = useState("");
  const [status, setStatus] = useState<ShareStatus>({ kind: "idle" });
  const [inviteEmail, setInviteEmail] = useState("");
  const [inviteRole, setInviteRole] = useState<InviteRole>("viewer");
  const [inviteError, setInviteError] = useState<string | null>(null);
  const [invites, setInvites] = useState<readonly { email: string; role: InviteRole }[]>([]);

  // Reset form state only when navigating between catalog items. Access edits
  // are managed locally so a successful apply can keep its status visible.
  // biome-ignore lint/correctness/useExhaustiveDependencies: item id is the reset boundary.
  useEffect(() => {
    setAccess({
      sharing: item.access.sharing,
      embeddable: item.access.embeddable,
      publicLinkToken: item.access.sharing === "public-link" ? defaultPublicLinkToken(item.id) : null,
    });
    setGroupId("");
    setStatus({ kind: "idle" });
  }, [item.id]);

  useEffect(() => {
    if (!canInviteRole(role, inviteRole)) setInviteRole("viewer");
  }, [inviteRole, role]);

  const shareUrl = useMemo(() => buildShareUrl(item, access), [item, access]);
  const canSubmitInvite = canInviteRole(role, inviteRole);

  async function handleApply(event: React.FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault();
    if (!editable) {
      setStatus({ kind: "unauthorized" });
      return;
    }
    const nextAccess = normalizeAccess(access, groupId);
    if (nextAccess.sharing === "group" && (!nextAccess.groupIds || nextAccess.groupIds.length === 0)) {
      setStatus({ kind: "error", message: "Choose a group before applying group sharing." });
      return;
    }
    if (item.access.openData && nextAccess.sharing !== "public") {
      setStatus({ kind: "error", message: "Open-data items must stay public until open-data export is disabled." });
      return;
    }

    setStatus({ kind: "saving" });
    try {
      const closure = await loadClosure();
      const review = reviewShareChange({
        current: item.access.sharing,
        proposed: nextAccess.sharing,
        closure,
      });
      if (review.kind === "blocked") {
        setStatus({ kind: "blocked", blockers: review.blockers });
        return;
      }

      const result = classifyPatchResult(await patchAccess(nextAccess));
      if (result.kind === "ok") {
        setAccess(result.access);
        onAccessChange(result.access);
        setStatus({ kind: "ok", access: result.access });
      } else if (result.kind === "blocked") {
        setStatus({
          kind: "blocked",
          blockers: result.blockers.map((blocker) => ({
            id: blocker.id,
            type: blocker.type,
            role: blocker.role,
            title: blocker.title,
            sharing: blocker.access && blocker.access !== "unsupported" ? blocker.access.sharing : undefined,
            reason: "narrower-tier",
          })),
        });
      } else if (result.kind === "unauthorized") {
        setStatus({ kind: "unauthorized" });
      } else {
        setStatus({ kind: "error", message: result.message });
      }
    } catch (error: unknown) {
      setStatus({ kind: "error", message: error instanceof Error ? error.message : String(error) });
    }
  }

  function handleSharingChange(value: SharingTier): void {
    setStatus({ kind: "idle" });
    setAccess((current) => ({
      ...current,
      sharing: value,
      publicLinkToken: value === "public-link" ? (current.publicLinkToken ?? defaultPublicLinkToken(item.id)) : null,
    }));
  }

  function handleInvite(event: React.FormEvent<HTMLFormElement>): void {
    event.preventDefault();
    const email = inviteEmail.trim();
    setInviteError(null);
    if (!canSubmitInvite) {
      setInviteError("Your role cannot invite that access level.");
      return;
    }
    if (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(email)) {
      setInviteError("Enter a valid email address.");
      return;
    }
    setInvites((current) => [...current, { email, role: inviteRole }]);
    setInviteEmail("");
    setInviteRole("viewer");
  }

  return (
    <section className="item-page__section share-panel" aria-labelledby="section-sharing" data-testid="share-panel">
      <div className="share-panel__heading">
        <div>
          <h2 id="section-sharing">Sharing</h2>
          <p className="share-panel__role">
            Current role: <strong>{roleEntry.label}</strong>
          </p>
        </div>
        <VisibilityPill sharing={access.sharing} />
      </div>

      {!editable ? (
        <p className="share-panel__notice" role="note">
          You can view this item, but only owners and editors can change sharing or invite collaborators.
        </p>
      ) : null}

      <div className="share-panel__layout">
        <form className="share-panel__form" onSubmit={(event) => void handleApply(event)}>
          <label className="share-panel__field">
            <span>Visibility</span>
            <select
              value={access.sharing}
              onChange={(event) => handleSharingChange(event.target.value as SharingTier)}
              disabled={!editable || status.kind === "saving"}
            >
              {SHARING_OPTIONS.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </label>

          {access.sharing === "group" ? (
            <label className="share-panel__field">
              <span>Group ID</span>
              <input
                type="text"
                value={groupId}
                onChange={(event) => setGroupId(event.target.value)}
                disabled={!editable || status.kind === "saving"}
                placeholder="group-id"
              />
            </label>
          ) : null}

          <label className="share-panel__check">
            <input
              type="checkbox"
              checked={access.embeddable}
              onChange={(event) => setAccess((current) => ({ ...current, embeddable: event.target.checked }))}
              disabled={!editable || status.kind === "saving"}
            />
            <span>Allow embeds</span>
          </label>

          <button type="submit" className="hc-btn hc-btn--primary" disabled={!editable || status.kind === "saving"}>
            {status.kind === "saving" ? "Applying..." : "Apply sharing"}
          </button>
        </form>

        <form className="share-panel__form" onSubmit={handleInvite}>
          <label className="share-panel__field">
            <span>Email</span>
            <input
              type="email"
              value={inviteEmail}
              onChange={(event) => setInviteEmail(event.target.value)}
              disabled={!canSubmitInvite}
              placeholder="name@example.com"
              aria-label="Invite email"
            />
          </label>
          <label className="share-panel__field">
            <span>Role</span>
            <select
              value={inviteRole}
              onChange={(event) => setInviteRole(event.target.value as InviteRole)}
              disabled={!editable}
              aria-label="Invite role"
            >
              <option value="viewer" disabled={!canInviteRole(role, "viewer")}>
                Viewer
              </option>
              <option value="editor" disabled={!canInviteRole(role, "editor")}>
                Editor
              </option>
            </select>
          </label>
          <button type="submit" className="hc-btn" disabled={!canSubmitInvite}>
            Add invite
          </button>
          {inviteError ? (
            <p className="share-panel__status share-panel__status--error" role="alert">
              {inviteError}
            </p>
          ) : null}
          {invites.length > 0 ? (
            <ul className="share-panel__invites" data-testid="pending-invites">
              {invites.map((invite) => (
                <li key={`${invite.email}:${invite.role}`}>
                  {invite.email} · {roleMatrixEntry(invite.role).label}
                </li>
              ))}
            </ul>
          ) : null}
        </form>
      </div>

      <ShareStatusMessage status={status} />

      {shareUrl ? (
        <div className="share-panel__copy">
          <CopyRow
            label={access.sharing === "public-link" ? "Public link" : "Public URL"}
            value={shareUrl}
            description={`Visible to ${visibilityLabel(access.sharing).toLowerCase()} audiences.`}
          />
        </div>
      ) : null}

      <RoleMatrix currentRole={role} />
    </section>
  );
}

function RoleMatrix({ currentRole }: { readonly currentRole: string }): JSX.Element {
  return (
    <table className="share-panel__matrix">
      <caption>Role matrix</caption>
      <thead>
        <tr>
          <th scope="col">Role</th>
          <th scope="col">Read</th>
          <th scope="col">Edit</th>
          <th scope="col">Share</th>
          <th scope="col">Invite editors</th>
          <th scope="col">Invite viewers</th>
        </tr>
      </thead>
      <tbody>
        {ROLE_MATRIX.map((entry) => (
          <tr key={entry.role} data-current={entry.role === currentRole}>
            <th scope="row">
              {entry.label}
              <span>{entry.description}</span>
            </th>
            <td>{yesNo(entry.permissions.read)}</td>
            <td>{yesNo(entry.permissions.editMetadata)}</td>
            <td>{yesNo(entry.permissions.updateSharing)}</td>
            <td>{yesNo(entry.permissions.inviteEditors)}</td>
            <td>{yesNo(entry.permissions.inviteViewers)}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

function ShareStatusMessage({ status }: { readonly status: ShareStatus }): JSX.Element | null {
  if (status.kind === "idle" || status.kind === "saving") return null;
  if (status.kind === "ok") {
    return (
      <output className="share-panel__status share-panel__status--ok" data-testid="share-status">
        Sharing updated to {visibilityLabel(status.access.sharing)}.
      </output>
    );
  }
  if (status.kind === "unauthorized") {
    return (
      <p className="share-panel__status share-panel__status--error" role="alert" data-testid="share-status">
        Access denied. Your role cannot update sharing for this item.
      </p>
    );
  }
  if (status.kind === "error") {
    return (
      <p className="share-panel__status share-panel__status--error" role="alert" data-testid="share-status">
        {status.message}
      </p>
    );
  }
  return (
    <div className="share-panel__status share-panel__status--blocked" role="alert" data-testid="share-status">
      <p>Sharing blocked until dependencies are visible to the selected audience.</p>
      <ul>
        {status.blockers.map((blocker) => (
          <li key={`${blocker.reason}:${blocker.id}`}>
            {blocker.title ?? blocker.id} · {blockerLabel(blocker)}
          </li>
        ))}
      </ul>
    </div>
  );
}

function itemToShareAccess(item: ContentItem): ShareAccess {
  return {
    sharing: item.access.sharing,
    embeddable: item.access.embeddable,
    publicLinkToken: item.access.sharing === "public-link" ? defaultPublicLinkToken(item.id) : null,
  };
}

function normalizeAccess(access: ShareAccess, groupId: string): ShareAccess {
  const normalized: ShareAccess = {
    sharing: access.sharing,
    embeddable: access.embeddable,
    publicLinkToken: access.sharing === "public-link" ? (access.publicLinkToken ?? null) : null,
  };
  const trimmedGroupId = groupId.trim();
  if (access.sharing === "group" && trimmedGroupId) normalized.groupIds = [trimmedGroupId];
  return normalized;
}

function buildShareUrl(item: ContentItem, access: ShareAccess): string | null {
  if (access.sharing !== "public" && access.sharing !== "public-link") return null;
  const path = `/catalog/${encodeURIComponent(item.slug ?? item.id)}`;
  const origin = typeof window === "undefined" ? "https://portal.honua.example" : window.location.origin;
  if (access.sharing === "public-link") {
    const token = access.publicLinkToken ?? defaultPublicLinkToken(item.id);
    return `${origin}${path}?share=${encodeURIComponent(token)}`;
  }
  return `${origin}${path}`;
}

function defaultPublicLinkToken(itemId: string): string {
  return `fixture-${itemId.slice(-8).toLowerCase()}`;
}

function blockerLabel(blocker: ShareReviewBlocker): string {
  switch (blocker.reason) {
    case "narrower-tier":
      return `currently ${blocker.sharing ?? "not shared"}`;
    case "unauthorized":
      return "revoked or denied dependency";
    case "missing":
      return "missing dependency";
    case "truncated":
      return "dependency review incomplete";
  }
}

function yesNo(value: boolean): JSX.Element {
  return value ? <Pill tone="success">Yes</Pill> : <Pill tone="muted">No</Pill>;
}
