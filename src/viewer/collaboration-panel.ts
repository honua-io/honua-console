export type CollaborationRole = "owner" | "editor" | "viewer";
export type CollaborationStatus = "active" | "idle" | "offline";

export interface CollaborationCursor {
  x: number;
  y: number;
  label?: string;
}

export interface CollaborationFeatureRef {
  layerId: string;
  featureId: string;
  label?: string;
}

export interface CollaborationParticipant {
  id: string;
  name: string;
  role: CollaborationRole;
  status: CollaborationStatus;
  color?: string;
  cursor?: CollaborationCursor;
  editing?: CollaborationFeatureRef;
  selecting?: CollaborationFeatureRef;
}

export interface CollaborationFeatureActivity extends CollaborationFeatureRef {
  editingBy?: string;
  selectedBy?: string[];
}

export interface CollaborationSessionSnapshot {
  currentUser: CollaborationParticipant;
  collaborators: CollaborationParticipant[];
  followingUserId?: string;
  featureActivities?: CollaborationFeatureActivity[];
  updatedAt?: string;
}

export interface CollaborationPanelOptions {
  onFollowUser?: (userId: string) => void;
  onUnfollowUser?: () => void;
  onFocusFeature?: (feature: CollaborationFeatureRef) => void;
}

export interface CollaborationPanel {
  render: (snapshot: CollaborationSessionSnapshot) => void;
  destroy: () => void;
}

export interface CollaborationCursorLayer {
  render: (snapshot: CollaborationSessionSnapshot) => void;
  destroy: () => void;
}

export interface CollaborationTableHighlightOptions {
  layerId: string;
  featureIdSelector?: string;
}

export function createCollaborationPanel(
  host: HTMLElement,
  options: CollaborationPanelOptions = {},
): CollaborationPanel {
  const abort = new AbortController();

  function render(snapshot: CollaborationSessionSnapshot): void {
    host.innerHTML = "";
    host.classList.add("collaboration-panel");
    host.setAttribute("data-collab-panel", "");
    host.setAttribute("role", "region");
    host.setAttribute("aria-label", "Collaboration");

    const participants = [snapshot.currentUser, ...snapshot.collaborators];
    host.appendChild(renderSummary(snapshot, participants));
    host.appendChild(renderParticipantList(snapshot, participants));
    host.appendChild(renderFeatureActivity(snapshot));
  }

  function renderSummary(
    snapshot: CollaborationSessionSnapshot,
    participants: ReadonlyArray<CollaborationParticipant>,
  ): HTMLElement {
    const summary = document.createElement("div");
    summary.className = "collaboration-panel__summary";
    summary.setAttribute("aria-live", "polite");

    const activeCount = participants.filter((participant) => participant.status === "active").length;
    summary.appendChild(metric(participants.length.toString(), participants.length === 1 ? "Person" : "People"));
    summary.appendChild(metric(activeCount.toString(), activeCount === 1 ? "Active" : "Active"));

    const current = document.createElement("span");
    current.className = "collaboration-panel__current";
    current.dataset["collabCurrentUser"] = snapshot.currentUser.id;
    current.textContent = `You: ${snapshot.currentUser.name}`;
    summary.appendChild(current);

    if (snapshot.followingUserId) {
      const followed = participants.find((participant) => participant.id === snapshot.followingUserId);
      const followState = document.createElement("span");
      followState.className = "collaboration-panel__follow-state";
      followState.dataset["collabFollowing"] = snapshot.followingUserId;
      followState.textContent = `Following ${followed?.name ?? "collaborator"}`;
      summary.appendChild(followState);
    }

    return summary;
  }

  function renderParticipantList(
    snapshot: CollaborationSessionSnapshot,
    participants: ReadonlyArray<CollaborationParticipant>,
  ): HTMLElement {
    const section = document.createElement("section");
    section.className = "collaboration-panel__section";

    const heading = document.createElement("h3");
    heading.className = "collaboration-panel__heading";
    heading.textContent = "Who is here";

    const list = document.createElement("ul");
    list.className = "collaboration-panel__people";
    list.dataset["collabPeople"] = "";

    for (const participant of participants) {
      list.appendChild(renderParticipant(snapshot, participant));
    }

    section.append(heading, list);
    return section;
  }

  function renderParticipant(
    snapshot: CollaborationSessionSnapshot,
    participant: CollaborationParticipant,
  ): HTMLLIElement {
    const item = document.createElement("li");
    item.className = "collaboration-person";
    item.dataset["collabUserId"] = participant.id;
    item.dataset["status"] = participant.status;
    item.dataset["role"] = participant.role;

    const swatch = document.createElement("span");
    swatch.className = "collaboration-person__swatch";
    swatch.setAttribute("aria-hidden", "true");
    swatch.style.backgroundColor = participant.color ?? fallbackColor(participant.id);

    const body = document.createElement("div");
    body.className = "collaboration-person__body";

    const main = document.createElement("div");
    main.className = "collaboration-person__main";
    const name = document.createElement("span");
    name.className = "collaboration-person__name";
    name.textContent = participant.id === snapshot.currentUser.id ? `${participant.name} (you)` : participant.name;
    const badges = document.createElement("span");
    badges.className = "collaboration-person__badges";
    badges.append(badge(roleLabel(participant.role), "role"), badge(statusLabel(participant.status), "status"));
    main.append(name, badges);

    const activity = document.createElement("p");
    activity.className = "collaboration-person__activity";
    activity.textContent = participantActivityLabel(participant);
    body.append(main, activity);

    item.append(swatch, body);
    if (participant.id !== snapshot.currentUser.id) {
      item.appendChild(renderFollowButton(snapshot, participant));
    }
    return item;
  }

  function renderFollowButton(
    snapshot: CollaborationSessionSnapshot,
    participant: CollaborationParticipant,
  ): HTMLButtonElement {
    const button = document.createElement("button");
    const isFollowing = snapshot.followingUserId === participant.id;
    button.type = "button";
    button.className = "collaboration-panel__action";
    button.dataset["collabFollowButton"] = participant.id;
    button.setAttribute("aria-pressed", isFollowing ? "true" : "false");
    button.setAttribute(
      "aria-label",
      isFollowing ? `Stop following ${participant.name}` : `Follow ${participant.name}`,
    );
    button.textContent = isFollowing ? "Unfollow" : "Follow";
    button.disabled = participant.status === "offline";
    button.addEventListener(
      "click",
      () => {
        if (isFollowing) {
          options.onUnfollowUser?.();
        } else {
          options.onFollowUser?.(participant.id);
        }
      },
      { signal: abort.signal },
    );
    return button;
  }

  function renderFeatureActivity(snapshot: CollaborationSessionSnapshot): HTMLElement {
    const section = document.createElement("section");
    section.className = "collaboration-panel__section";

    const heading = document.createElement("h3");
    heading.className = "collaboration-panel__heading";
    heading.textContent = "Feature activity";
    section.appendChild(heading);

    const activities = getFeatureActivities(snapshot);
    if (activities.length === 0) {
      const empty = document.createElement("p");
      empty.className = "empty-copy";
      empty.dataset["collabEmpty"] = "";
      empty.textContent = "No one is editing or selecting features right now.";
      section.appendChild(empty);
      return section;
    }

    const list = document.createElement("ul");
    list.className = "collaboration-panel__features";
    list.dataset["collabFeatureList"] = "";
    for (const activity of activities) {
      list.appendChild(renderFeatureActivityItem(snapshot, activity));
    }
    section.appendChild(list);
    return section;
  }

  function renderFeatureActivityItem(
    snapshot: CollaborationSessionSnapshot,
    activity: CollaborationFeatureActivity,
  ): HTMLLIElement {
    const participants = [snapshot.currentUser, ...snapshot.collaborators];
    const item = document.createElement("li");
    item.className = "collaboration-feature";
    item.dataset["collabFeatureActivity"] = `${activity.layerId}:${activity.featureId}`;
    if (activity.editingBy) item.dataset["editingBy"] = activity.editingBy;
    if (activity.selectedBy && activity.selectedBy.length > 0)
      item.dataset["selectedBy"] = activity.selectedBy.join(",");

    const body = document.createElement("div");
    body.className = "collaboration-feature__body";
    const title = document.createElement("span");
    title.className = "collaboration-feature__title";
    title.textContent = activity.label ?? activity.featureId;
    const meta = document.createElement("span");
    meta.className = "collaboration-feature__meta";
    meta.textContent = featureActivityLabel(activity, participants);
    body.append(title, meta);

    const button = document.createElement("button");
    button.type = "button";
    button.className = "collaboration-panel__action collaboration-panel__action--subtle";
    button.dataset["collabFocusFeature"] = `${activity.layerId}:${activity.featureId}`;
    button.setAttribute("aria-label", `Zoom to ${activity.label ?? `feature ${activity.featureId}`}`);
    button.textContent = "Zoom";
    button.addEventListener(
      "click",
      () =>
        options.onFocusFeature?.({ layerId: activity.layerId, featureId: activity.featureId, label: activity.label }),
      { signal: abort.signal },
    );

    item.append(body, button);
    return item;
  }

  return {
    render,
    destroy: () => {
      abort.abort();
      host.innerHTML = "";
      host.classList.remove("collaboration-panel");
      host.removeAttribute("data-collab-panel");
      host.removeAttribute("role");
      host.removeAttribute("aria-label");
    },
  };
}

export function createCollaborationCursorLayer(host: HTMLElement): CollaborationCursorLayer {
  function render(snapshot: CollaborationSessionSnapshot): void {
    host.innerHTML = "";
    host.classList.add("collaboration-cursors");
    host.setAttribute("data-collab-cursors", "");
    host.setAttribute("aria-hidden", "true");

    for (const participant of snapshot.collaborators) {
      if (!participant.cursor || participant.status === "offline") continue;
      host.appendChild(renderCursor(participant));
    }
  }

  return {
    render,
    destroy: () => {
      host.innerHTML = "";
      host.classList.remove("collaboration-cursors");
      host.removeAttribute("data-collab-cursors");
      host.removeAttribute("aria-hidden");
    },
  };
}

export function applyCollaborationTableHighlights(
  tableBody: HTMLElement,
  snapshot: CollaborationSessionSnapshot,
  options: CollaborationTableHighlightOptions,
): void {
  const selector = options.featureIdSelector ?? "tr[data-feature-id]";
  const activities = getFeatureActivities(snapshot).filter((activity) => activity.layerId === options.layerId);
  const byFeature = new Map(activities.map((activity) => [activity.featureId, activity]));

  tableBody.querySelectorAll<HTMLElement>(selector).forEach((row) => {
    row.removeAttribute("data-collab-editing-by");
    row.removeAttribute("data-collab-selected-by");
    const featureId = row.dataset["featureId"];
    if (!featureId) return;
    const activity = byFeature.get(featureId);
    if (!activity) return;
    if (activity.editingBy) row.dataset["collabEditingBy"] = activity.editingBy;
    if (activity.selectedBy && activity.selectedBy.length > 0) {
      row.dataset["collabSelectedBy"] = activity.selectedBy.join(",");
    }
  });
}

function renderCursor(participant: CollaborationParticipant): HTMLElement {
  const cursor = document.createElement("div");
  cursor.className = "collaboration-cursor";
  cursor.dataset["collabCursor"] = participant.id;
  cursor.style.left = `${participant.cursor?.x ?? 0}px`;
  cursor.style.top = `${participant.cursor?.y ?? 0}px`;
  cursor.style.color = participant.color ?? fallbackColor(participant.id);

  const pointer = document.createElement("span");
  pointer.className = "collaboration-cursor__pointer";

  const label = document.createElement("span");
  label.className = "collaboration-cursor__label";
  label.textContent = participant.cursor?.label ?? participant.name;

  cursor.append(pointer, label);
  return cursor;
}

function deriveFeatureActivities(snapshot: CollaborationSessionSnapshot): CollaborationFeatureActivity[] {
  const byFeature = new Map<string, CollaborationFeatureActivity>();
  for (const participant of [snapshot.currentUser, ...snapshot.collaborators]) {
    if (participant.editing) {
      const activity = getActivity(byFeature, participant.editing);
      activity.editingBy = participant.id;
    }
    if (participant.selecting) {
      const activity = getActivity(byFeature, participant.selecting);
      activity.selectedBy = [...(activity.selectedBy ?? []), participant.id];
    }
  }
  return [...byFeature.values()];
}

function getFeatureActivities(snapshot: CollaborationSessionSnapshot): CollaborationFeatureActivity[] {
  return snapshot.featureActivities ?? deriveFeatureActivities(snapshot);
}

function getActivity(
  byFeature: Map<string, CollaborationFeatureActivity>,
  feature: CollaborationFeatureRef,
): CollaborationFeatureActivity {
  const key = `${feature.layerId}:${feature.featureId}`;
  const existing = byFeature.get(key);
  if (existing) return existing;
  const activity = { ...feature };
  byFeature.set(key, activity);
  return activity;
}

function participantActivityLabel(participant: CollaborationParticipant): string {
  if (participant.editing) return `Editing ${participant.editing.label ?? participant.editing.featureId}`;
  if (participant.selecting) return `Selected ${participant.selecting.label ?? participant.selecting.featureId}`;
  if (participant.cursor) return participant.cursor.label ?? "Cursor on map";
  return participant.status === "offline" ? "Not currently connected" : "Viewing map";
}

function featureActivityLabel(
  activity: CollaborationFeatureActivity,
  participants: ReadonlyArray<CollaborationParticipant>,
): string {
  const parts: string[] = [];
  if (activity.editingBy) parts.push(`${participantName(activity.editingBy, participants)} editing`);
  if (activity.selectedBy && activity.selectedBy.length > 0) {
    parts.push(`${activity.selectedBy.map((id) => participantName(id, participants)).join(", ")} selected`);
  }
  return parts.join(" · ");
}

function participantName(id: string, participants: ReadonlyArray<CollaborationParticipant>): string {
  return participants.find((participant) => participant.id === id)?.name ?? id;
}

function metric(value: string, label: string): HTMLElement {
  const element = document.createElement("span");
  element.className = "collaboration-panel__metric";
  const valueElement = document.createElement("strong");
  valueElement.textContent = value;
  const labelElement = document.createElement("span");
  labelElement.textContent = label;
  element.append(valueElement, labelElement);
  return element;
}

function badge(label: string, type: "role" | "status"): HTMLElement {
  const element = document.createElement("span");
  element.className = "collaboration-person__badge";
  element.dataset["badge"] = type;
  element.textContent = label;
  return element;
}

function roleLabel(role: CollaborationRole): string {
  switch (role) {
    case "owner":
      return "Owner";
    case "editor":
      return "Editor";
    case "viewer":
      return "Viewer";
  }
}

function statusLabel(status: CollaborationStatus): string {
  switch (status) {
    case "active":
      return "Active";
    case "idle":
      return "Idle";
    case "offline":
      return "Offline";
  }
}

function fallbackColor(seed: string): string {
  const colors = ["#4ec9b0", "#f3b562", "#7aa7ff", "#ff9a9a", "#b694ff"];
  let hash = 0;
  for (const char of seed) hash = (hash + char.charCodeAt(0)) % colors.length;
  return colors[hash];
}
