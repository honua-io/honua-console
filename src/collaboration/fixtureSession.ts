import type {
  CollaborationActor,
  CollaborationEventEnvelope,
  CollaborationParticipant,
  CollaborationParticipantStatus,
  CollaborationSession,
  CollaborationSnapshot,
  CollaborationSnapshotListener,
  FeatureEditLock,
  FeatureRef,
  FeatureSelection,
  FollowTarget,
  LiveCursor,
} from "./types.js";

const DEFAULT_HEARTBEAT_MS = 5_000;
const DEFAULT_PEER_TTL_MS = 20_000;
const DEFAULT_LOCK_TTL_MS = 30_000;
const STORAGE_PREFIX = "honua:collaboration";
const LOCAL_EVENT_PREFIX = "honua-collaboration-message";

export interface FixtureCollaborationSessionOptions {
  heartbeatMs?: number;
  peerTtlMs?: number;
  lockTtlMs?: number;
  now?: () => number;
  generateSessionId?: () => string;
}

interface Transport {
  post(envelope: CollaborationEventEnvelope): void;
  dispose(): void;
}

type MessageHandler = (envelope: CollaborationEventEnvelope) => void;

function featureKey(feature: FeatureRef): string {
  return `${feature.layerId}:${feature.featureId}`;
}

function makeSessionId(): string {
  if (typeof crypto !== "undefined" && "randomUUID" in crypto) {
    return crypto.randomUUID();
  }
  return `fixture-${Math.random().toString(36).slice(2)}`;
}

function cloneSnapshot(snapshot: CollaborationSnapshot): CollaborationSnapshot {
  return {
    ...snapshot,
    participants: { ...snapshot.participants },
    cursors: { ...snapshot.cursors },
    selections: { ...snapshot.selections },
    editLocks: { ...snapshot.editLocks },
    followTarget: snapshot.followTarget ? { ...snapshot.followTarget } : null,
  };
}

function makeParticipant(actor: CollaborationActor, now: number): CollaborationParticipant {
  return {
    ...actor,
    status: "active",
    joinedAt: now,
    lastSeenAt: now,
  };
}

function sameFeature(a: FeatureRef, b: FeatureRef): boolean {
  return a.layerId === b.layerId && a.featureId === b.featureId;
}

class BroadcastChannelTransport implements Transport {
  private readonly channel: BroadcastChannel;

  constructor(
    channelName: string,
    private readonly onMessage: MessageHandler,
  ) {
    this.channel = new BroadcastChannel(channelName);
    this.channel.addEventListener("message", this.handleMessage);
  }

  post(envelope: CollaborationEventEnvelope) {
    this.channel.postMessage(envelope);
  }

  dispose() {
    this.channel.removeEventListener("message", this.handleMessage);
    this.channel.close();
  }

  private readonly handleMessage = (event: MessageEvent<CollaborationEventEnvelope>) => {
    this.onMessage(event.data);
  };
}

class LocalStorageTransport implements Transport {
  private readonly localEventName: string;
  private readonly storageKey: string;

  constructor(
    channelName: string,
    private readonly onMessage: MessageHandler,
  ) {
    this.storageKey = `${STORAGE_PREFIX}:${channelName}`;
    this.localEventName = `${LOCAL_EVENT_PREFIX}:${channelName}`;
    window.addEventListener("storage", this.handleStorage);
    window.addEventListener(this.localEventName, this.handleLocal as EventListener);
  }

  post(envelope: CollaborationEventEnvelope) {
    const payload = JSON.stringify(envelope);
    localStorage.setItem(this.storageKey, payload);
    window.dispatchEvent(new CustomEvent(this.localEventName, { detail: payload }));
  }

  dispose() {
    window.removeEventListener("storage", this.handleStorage);
    window.removeEventListener(this.localEventName, this.handleLocal as EventListener);
  }

  private readonly emitPayload = (payload: string | null) => {
    if (!payload) {
      return;
    }
    this.onMessage(JSON.parse(payload) as CollaborationEventEnvelope);
  };

  private readonly handleStorage = (event: StorageEvent) => {
    if (event.key !== this.storageKey) {
      return;
    }
    this.emitPayload(event.newValue);
  };

  private readonly handleLocal = (event: CustomEvent<string>) => {
    this.emitPayload(event.detail);
  };
}

class FixtureCollaborationSession implements CollaborationSession {
  readonly savedMapId: string;
  readonly actorId: string;

  private disposed = false;
  private sequence = 0;
  private readonly sessionId: string;
  private readonly heartbeatMs: number;
  private readonly peerTtlMs: number;
  private readonly lockTtlMs: number;
  private readonly now: () => number;
  private readonly transport: Transport;
  private readonly listeners = new Set<CollaborationSnapshotListener>();
  private readonly heartbeatTimer: ReturnType<typeof setInterval>;
  private snapshot: CollaborationSnapshot;

  constructor(savedMapId: string, actor: CollaborationActor, options: FixtureCollaborationSessionOptions = {}) {
    this.savedMapId = savedMapId;
    this.actorId = actor.id;
    this.heartbeatMs = options.heartbeatMs ?? DEFAULT_HEARTBEAT_MS;
    this.peerTtlMs = options.peerTtlMs ?? DEFAULT_PEER_TTL_MS;
    this.lockTtlMs = options.lockTtlMs ?? DEFAULT_LOCK_TTL_MS;
    this.now = options.now ?? Date.now;
    this.sessionId = options.generateSessionId?.() ?? makeSessionId();

    const joinedAt = this.now();
    const participant = makeParticipant(actor, joinedAt);
    this.snapshot = {
      savedMapId,
      actorId: actor.id,
      participants: { [actor.id]: participant },
      cursors: {},
      selections: {},
      editLocks: {},
      followTarget: null,
      revision: 0,
      updatedAt: joinedAt,
    };

    const channelName = `${STORAGE_PREFIX}:${savedMapId}`;
    this.transport =
      typeof BroadcastChannel === "undefined"
        ? new LocalStorageTransport(channelName, this.receive)
        : new BroadcastChannelTransport(channelName, this.receive);

    this.heartbeatTimer = setInterval(this.heartbeat, this.heartbeatMs);
    this.broadcast({ type: "participant-joined", participant });
  }

  getSnapshot(): CollaborationSnapshot {
    this.pruneStaleState();
    return cloneSnapshot(this.snapshot);
  }

  subscribe(listener: CollaborationSnapshotListener): () => void {
    this.listeners.add(listener);
    listener(this.getSnapshot());
    return () => {
      this.listeners.delete(listener);
    };
  }

  updateParticipant(
    update: Partial<Pick<CollaborationActor, "displayName" | "email" | "color" | "role">> & {
      status?: CollaborationParticipantStatus;
    },
  ) {
    const current = this.snapshot.participants[this.actorId];
    if (!current) {
      return;
    }
    const participant = { ...current, ...update, lastSeenAt: this.now() };
    this.applyParticipant(participant);
    this.broadcast({ type: "participant-updated", participant });
  }

  publishCursor(cursor: Omit<LiveCursor, "participantId" | "updatedAt"> | null) {
    this.touchSelf();
    const updated = cursor ? { ...cursor, participantId: this.actorId, updatedAt: this.now() } : null;
    this.applyCursor(this.actorId, updated);
    this.broadcast({ type: "cursor-updated", participantId: this.actorId, cursor: updated });
  }

  selectFeature(feature: FeatureRef) {
    this.touchSelf();
    const now = this.now();
    const selection: FeatureSelection = {
      feature,
      participantId: this.actorId,
      selectedAt: now,
      updatedAt: now,
    };
    this.applySelection(selection);
    this.broadcast({ type: "feature-selected", selection });
  }

  clearFeatureSelection(feature?: FeatureRef) {
    this.touchSelf();
    this.applySelectionClear(this.actorId, feature);
    this.broadcast({ type: "feature-selection-cleared", participantId: this.actorId, feature });
  }

  claimFeature(feature: FeatureRef): boolean {
    this.pruneStaleState();
    const key = featureKey(feature);
    const current = this.snapshot.editLocks[key];
    if (current && current.participantId !== this.actorId) {
      return false;
    }
    this.touchSelf();
    const now = this.now();
    const lock: FeatureEditLock = {
      feature,
      participantId: this.actorId,
      claimedAt: current?.claimedAt ?? now,
      updatedAt: now,
      expiresAt: now + this.lockTtlMs,
    };
    this.applyLock(lock);
    this.broadcast({ type: "feature-lock-claimed", lock });
    return true;
  }

  releaseFeature(feature: FeatureRef) {
    this.touchSelf();
    this.applyLockRelease(this.actorId, feature);
    this.broadcast({ type: "feature-lock-released", participantId: this.actorId, feature });
  }

  follow(participantId: string) {
    if (participantId === this.actorId) {
      return;
    }
    const target: FollowTarget = { participantId, since: this.now() };
    this.snapshot = { ...this.snapshot, followTarget: target };
    this.commit();
    this.broadcast({ type: "follow-started", followerId: this.actorId, target });
  }

  unfollow() {
    this.snapshot = { ...this.snapshot, followTarget: null };
    this.commit();
    this.broadcast({ type: "follow-stopped", followerId: this.actorId });
  }

  dispose() {
    if (this.disposed) {
      return;
    }
    this.disposed = true;
    clearInterval(this.heartbeatTimer);
    this.broadcast({ type: "participant-left", participantId: this.actorId });
    this.transport.dispose();
    this.listeners.clear();
  }

  private readonly heartbeat = () => {
    this.touchSelf();
    this.announceSelf();
    this.pruneStaleState();
  };

  private announceSelf() {
    const participant = this.snapshot.participants[this.actorId];
    if (participant) {
      this.broadcast({ type: "participant-updated", participant });
    }
  }

  private readonly receive = (envelope: CollaborationEventEnvelope) => {
    if (this.disposed || envelope.savedMapId !== this.savedMapId || envelope.sourceSessionId === this.sessionId) {
      return;
    }

    switch (envelope.event.type) {
      case "participant-joined":
        this.applyParticipant(envelope.event.participant);
        this.announceSelf();
        break;
      case "participant-updated":
        this.applyParticipant(envelope.event.participant);
        break;
      case "participant-left":
        this.applyParticipantLeft(envelope.event.participantId);
        break;
      case "cursor-updated":
        this.applyCursor(envelope.event.participantId, envelope.event.cursor);
        break;
      case "feature-selected":
        this.applySelection(envelope.event.selection);
        break;
      case "feature-selection-cleared":
        this.applySelectionClear(envelope.event.participantId, envelope.event.feature);
        break;
      case "feature-lock-claimed":
        this.applyLock(envelope.event.lock);
        break;
      case "feature-lock-released":
        this.applyLockRelease(envelope.event.participantId, envelope.event.feature);
        break;
      case "follow-started":
      case "follow-stopped":
        break;
    }
    this.pruneStaleState();
  };

  private touchSelf() {
    const current = this.snapshot.participants[this.actorId];
    if (!current) {
      return;
    }
    this.applyParticipant({ ...current, lastSeenAt: this.now() });
  }

  private applyParticipant(participant: CollaborationParticipant) {
    const current = this.snapshot.participants[participant.id];
    if (current && current.lastSeenAt > participant.lastSeenAt) {
      return;
    }
    this.snapshot = {
      ...this.snapshot,
      participants: { ...this.snapshot.participants, [participant.id]: participant },
    };
    this.commit();
  }

  private applyParticipantLeft(participantId: string) {
    const participants = { ...this.snapshot.participants };
    const cursors = { ...this.snapshot.cursors };
    const selections = { ...this.snapshot.selections };
    const editLocks = { ...this.snapshot.editLocks };
    delete participants[participantId];
    delete cursors[participantId];
    for (const [key, selection] of Object.entries(selections)) {
      if (selection.participantId === participantId) {
        delete selections[key];
      }
    }
    for (const [key, lock] of Object.entries(editLocks)) {
      if (lock.participantId === participantId) {
        delete editLocks[key];
      }
    }
    this.snapshot = { ...this.snapshot, participants, cursors, selections, editLocks };
    this.commit();
  }

  private applyCursor(participantId: string, cursor: LiveCursor | null) {
    const cursors = { ...this.snapshot.cursors };
    if (cursor) {
      cursors[participantId] = cursor;
    } else {
      delete cursors[participantId];
    }
    this.snapshot = { ...this.snapshot, cursors };
    this.commit();
  }

  private applySelection(selection: FeatureSelection) {
    const selections = { ...this.snapshot.selections };
    for (const [key, current] of Object.entries(selections)) {
      if (current.participantId === selection.participantId) {
        delete selections[key];
      }
    }
    selections[featureKey(selection.feature)] = selection;
    this.snapshot = { ...this.snapshot, selections };
    this.commit();
  }

  private applySelectionClear(participantId: string, feature?: FeatureRef) {
    const selections = { ...this.snapshot.selections };
    for (const [key, current] of Object.entries(selections)) {
      if (current.participantId === participantId && (!feature || sameFeature(current.feature, feature))) {
        delete selections[key];
      }
    }
    this.snapshot = { ...this.snapshot, selections };
    this.commit();
  }

  private applyLock(lock: FeatureEditLock) {
    const key = featureKey(lock.feature);
    const current = this.snapshot.editLocks[key];
    if (current && current.participantId !== lock.participantId && current.expiresAt > this.now()) {
      return;
    }
    this.snapshot = {
      ...this.snapshot,
      editLocks: { ...this.snapshot.editLocks, [key]: lock },
    };
    this.commit();
  }

  private applyLockRelease(participantId: string, feature: FeatureRef) {
    const key = featureKey(feature);
    const current = this.snapshot.editLocks[key];
    if (!current || current.participantId !== participantId) {
      return;
    }
    const editLocks = { ...this.snapshot.editLocks };
    delete editLocks[key];
    this.snapshot = { ...this.snapshot, editLocks };
    this.commit();
  }

  private pruneStaleState() {
    const now = this.now();
    const participants = { ...this.snapshot.participants };
    const cursors = { ...this.snapshot.cursors };
    const selections = { ...this.snapshot.selections };
    const editLocks = { ...this.snapshot.editLocks };
    let changed = false;

    for (const [participantId, participant] of Object.entries(participants)) {
      if (participantId !== this.actorId && now - participant.lastSeenAt > this.peerTtlMs) {
        delete participants[participantId];
        delete cursors[participantId];
        changed = true;
      }
    }

    for (const [key, selection] of Object.entries(selections)) {
      if (!participants[selection.participantId]) {
        delete selections[key];
        changed = true;
      }
    }

    for (const [key, lock] of Object.entries(editLocks)) {
      if (!participants[lock.participantId] || lock.expiresAt <= now) {
        delete editLocks[key];
        changed = true;
      }
    }

    const followTarget =
      this.snapshot.followTarget && participants[this.snapshot.followTarget.participantId]
        ? this.snapshot.followTarget
        : null;
    changed ||= followTarget !== this.snapshot.followTarget;

    if (changed) {
      this.snapshot = { ...this.snapshot, participants, cursors, selections, editLocks, followTarget };
      this.commit();
    }
  }

  private broadcast(event: CollaborationEventEnvelope["event"]) {
    if (this.disposed && event.type !== "participant-left") {
      return;
    }
    this.transport.post({
      savedMapId: this.savedMapId,
      sourceSessionId: this.sessionId,
      sequence: ++this.sequence,
      sentAt: this.now(),
      event,
    });
  }

  private commit() {
    this.snapshot = {
      ...this.snapshot,
      revision: this.snapshot.revision + 1,
      updatedAt: this.now(),
    };
    const snapshot = cloneSnapshot(this.snapshot);
    for (const listener of this.listeners) {
      listener(snapshot);
    }
  }
}

export function createFixtureCollaborationSession(
  savedMapId: string,
  actor: CollaborationActor,
  options?: FixtureCollaborationSessionOptions,
): CollaborationSession {
  return new FixtureCollaborationSession(savedMapId, actor, options);
}
