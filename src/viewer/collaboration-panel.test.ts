import { describe, expect, it, vi } from "vitest";
import {
  type CollaborationParticipant,
  type CollaborationSessionSnapshot,
  createCollaborationPanel,
} from "./collaboration-panel.js";

describe("createCollaborationPanel", () => {
  it("renders an empty feature activity state for a solo viewer", () => {
    const host = document.createElement("div");
    createCollaborationPanel(host).render(snapshot({ collaborators: [] }));

    expect(host).toHaveAttribute("role", "region");
    expect(host).toHaveAttribute("aria-label", "Collaboration");
    expect(host.querySelector("[data-collab-current-user]")).toHaveTextContent("You: Malia");
    expect(host.querySelector("[data-collab-empty]")).toHaveTextContent(
      "No one is editing or selecting features right now.",
    );
    expect(host.querySelectorAll("[data-collab-user-id]")).toHaveLength(1);
  });

  it("renders multiple users with roles, status, and map cursor activity", () => {
    const host = document.createElement("div");
    createCollaborationPanel(host).render(
      snapshot({
        collaborators: [
          participant({ id: "kai", name: "Kai", role: "editor", status: "active", cursor: { x: 14, y: 20 } }),
          participant({ id: "noe", name: "Noe", role: "viewer", status: "idle" }),
        ],
      }),
    );

    expect(host.querySelector("[data-collab-user-id='kai']")).toHaveTextContent("Editor");
    expect(host.querySelector("[data-collab-user-id='kai']")).toHaveTextContent("Cursor on map");
    expect(host.querySelector("[data-collab-user-id='noe']")).toHaveTextContent("Idle");
    expect(host.querySelectorAll("[data-collab-follow-button]")).toHaveLength(2);
  });

  it("renders edit-lock and selection indicators with zoom intent callbacks", () => {
    const host = document.createElement("div");
    const onFocusFeature = vi.fn();
    createCollaborationPanel(host, { onFocusFeature }).render(
      snapshot({
        collaborators: [
          participant({
            id: "kai",
            name: "Kai",
            editing: { layerId: "parcels", featureId: "lot-7", label: "Lot 7" },
          }),
          participant({
            id: "noe",
            name: "Noe",
            selecting: { layerId: "roads", featureId: "road-2", label: "Road 2" },
          }),
        ],
      }),
    );

    const editLock = host.querySelector("[data-collab-feature-activity='parcels:lot-7']");
    expect(editLock).toHaveAttribute("data-editing-by", "kai");
    expect(editLock).toHaveTextContent("Kai editing");

    const selection = host.querySelector("[data-collab-feature-activity='roads:road-2']");
    expect(selection).toHaveAttribute("data-selected-by", "noe");
    expect(selection).toHaveTextContent("Noe selected");

    host.querySelector<HTMLButtonElement>("[data-collab-focus-feature='parcels:lot-7']")?.click();
    expect(onFocusFeature).toHaveBeenCalledWith({ layerId: "parcels", featureId: "lot-7", label: "Lot 7" });
  });

  it("renders follow and unfollow affordances", () => {
    const host = document.createElement("div");
    const onFollowUser = vi.fn();
    const onUnfollowUser = vi.fn();
    createCollaborationPanel(host, { onFollowUser, onUnfollowUser }).render(
      snapshot({
        followingUserId: "kai",
        collaborators: [
          participant({ id: "kai", name: "Kai", status: "active" }),
          participant({ id: "noe", name: "Noe", status: "offline" }),
        ],
      }),
    );

    const kaiButton = host.querySelector<HTMLButtonElement>("[data-collab-follow-button='kai']");
    expect(kaiButton).toHaveTextContent("Unfollow");
    expect(kaiButton).toHaveAttribute("aria-pressed", "true");
    kaiButton?.click();
    expect(onUnfollowUser).toHaveBeenCalledTimes(1);

    const noeButton = host.querySelector<HTMLButtonElement>("[data-collab-follow-button='noe']");
    expect(noeButton).toBeDisabled();

    createCollaborationPanel(host, { onFollowUser, onUnfollowUser }).render(
      snapshot({ collaborators: [participant({ id: "kai", name: "Kai" })] }),
    );
    host.querySelector<HTMLButtonElement>("[data-collab-follow-button='kai']")?.click();
    expect(onFollowUser).toHaveBeenCalledWith("kai");
  });
});

function snapshot(overrides: Partial<CollaborationSessionSnapshot> = {}): CollaborationSessionSnapshot {
  return {
    currentUser: participant({ id: "malia", name: "Malia", role: "owner", status: "active" }),
    collaborators: [participant({ id: "kai", name: "Kai" })],
    ...overrides,
  };
}

function participant(overrides: Partial<CollaborationParticipant>): CollaborationParticipant {
  return {
    id: "user",
    name: "User",
    role: "editor",
    status: "active",
    ...overrides,
  };
}
