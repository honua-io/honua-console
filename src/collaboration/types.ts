export type CollaborationAccessRole = "view" | "comment" | "edit";

export type CollaborationParticipantStatus = "active" | "idle" | "away";

export interface CollaborationActor {
  id: string;
  displayName: string;
  email?: string;
  color?: string;
  role: CollaborationAccessRole;
}

export interface CollaborationParticipant extends CollaborationActor {
  status: CollaborationParticipantStatus;
  joinedAt: number;
  lastSeenAt: number;
}

export interface CollaborationPoint {
  lng: number;
  lat: number;
}

export interface CollaborationScreenPoint {
  x: number;
  y: number;
}

export interface LiveCursor {
  participantId: string;
  mapPoint: CollaborationPoint;
  screenPoint?: CollaborationScreenPoint;
  updatedAt: number;
}

export interface FeatureRef {
  layerId: string;
  featureId: string;
}

export interface FeatureSelection {
  feature: FeatureRef;
  participantId: string;
  selectedAt: number;
  updatedAt: number;
}

export interface FeatureEditLock {
  feature: FeatureRef;
  participantId: string;
  claimedAt: number;
  updatedAt: number;
  expiresAt: number;
}

export interface FollowTarget {
  participantId: string;
  since: number;
}

export interface CollaborationSnapshot {
  savedMapId: string;
  actorId: string;
  participants: Record<string, CollaborationParticipant>;
  cursors: Record<string, LiveCursor>;
  selections: Record<string, FeatureSelection>;
  editLocks: Record<string, FeatureEditLock>;
  followTarget: FollowTarget | null;
  revision: number;
  updatedAt: number;
}

export type CollaborationEvent =
  | {
      type: "participant-joined";
      participant: CollaborationParticipant;
    }
  | {
      type: "participant-updated";
      participant: CollaborationParticipant;
    }
  | {
      type: "participant-left";
      participantId: string;
    }
  | {
      type: "cursor-updated";
      cursor: LiveCursor | null;
      participantId: string;
    }
  | {
      type: "feature-selected";
      selection: FeatureSelection;
    }
  | {
      type: "feature-selection-cleared";
      participantId: string;
      feature?: FeatureRef;
    }
  | {
      type: "feature-lock-claimed";
      lock: FeatureEditLock;
    }
  | {
      type: "feature-lock-released";
      participantId: string;
      feature: FeatureRef;
    }
  | {
      type: "follow-started";
      followerId: string;
      target: FollowTarget;
    }
  | {
      type: "follow-stopped";
      followerId: string;
    };

export interface CollaborationEventEnvelope {
  savedMapId: string;
  sourceSessionId: string;
  sequence: number;
  sentAt: number;
  event: CollaborationEvent;
}

export type CollaborationSnapshotListener = (snapshot: CollaborationSnapshot) => void;

export interface CollaborationSession {
  readonly savedMapId: string;
  readonly actorId: string;
  getSnapshot(): CollaborationSnapshot;
  subscribe(listener: CollaborationSnapshotListener): () => void;
  updateParticipant(
    update: Partial<Pick<CollaborationActor, "displayName" | "email" | "color" | "role">> & {
      status?: CollaborationParticipantStatus;
    },
  ): void;
  publishCursor(cursor: Omit<LiveCursor, "participantId" | "updatedAt"> | null): void;
  selectFeature(feature: FeatureRef): void;
  clearFeatureSelection(feature?: FeatureRef): void;
  claimFeature(feature: FeatureRef): boolean;
  releaseFeature(feature: FeatureRef): void;
  follow(participantId: string): void;
  unfollow(): void;
  dispose(): void;
}
