import { canSeeOperatorLinks, hasAnyScope } from "../auth/permissions.js";
import type { Session } from "../auth/types.js";
import type { Owner } from "../contracts/content-item.js";

export type PortalItemRole = "owner" | "editor" | "viewer";
export type InviteRole = "editor" | "viewer";

export interface RoleMatrixEntry {
  readonly role: PortalItemRole;
  readonly label: string;
  readonly description: string;
  readonly permissions: {
    readonly read: boolean;
    readonly editMetadata: boolean;
    readonly updateSharing: boolean;
    readonly inviteEditors: boolean;
    readonly inviteViewers: boolean;
    readonly revokeAccess: boolean;
  };
}

export const ROLE_MATRIX: readonly RoleMatrixEntry[] = [
  {
    role: "owner",
    label: "Owner",
    description: "Full control over item visibility, editors, viewers, and revocation.",
    permissions: {
      read: true,
      editMetadata: true,
      updateSharing: true,
      inviteEditors: true,
      inviteViewers: true,
      revokeAccess: true,
    },
  },
  {
    role: "editor",
    label: "Editor",
    description: "Can maintain item metadata, update visibility, and invite viewers.",
    permissions: {
      read: true,
      editMetadata: true,
      updateSharing: true,
      inviteEditors: false,
      inviteViewers: true,
      revokeAccess: false,
    },
  },
  {
    role: "viewer",
    label: "Viewer",
    description: "Can read visible item content only.",
    permissions: {
      read: true,
      editMetadata: false,
      updateSharing: false,
      inviteEditors: false,
      inviteViewers: false,
      revokeAccess: false,
    },
  },
];

const ROLE_LOOKUP = new Map(ROLE_MATRIX.map((entry) => [entry.role, entry]));

export function resolvePortalItemRole(session: Session, item: { owner: Owner }): PortalItemRole {
  if (session.status !== "authenticated") return "viewer";
  if (item.owner.kind === "user" && item.owner.id === session.user.id) return "owner";
  if (canSeeOperatorLinks(session) || hasAnyScope(session, ["share:write", "catalog:write"])) return "editor";
  return "viewer";
}

export function roleMatrixEntry(role: PortalItemRole): RoleMatrixEntry {
  const entry = ROLE_LOOKUP.get(role);
  if (!entry) throw new Error(`unknown portal role: ${String(role)}`);
  return entry;
}

export function canUpdateSharing(role: PortalItemRole): boolean {
  return roleMatrixEntry(role).permissions.updateSharing;
}

export function canInviteRole(actorRole: PortalItemRole, targetRole: InviteRole): boolean {
  const permissions = roleMatrixEntry(actorRole).permissions;
  if (targetRole === "editor") return permissions.inviteEditors;
  return permissions.inviteViewers;
}
