import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { createFixtureCollaborationSession } from "./fixtureSession.js";
import type { CollaborationActor, CollaborationSession, FeatureRef } from "./types.js";

const alice: CollaborationActor = {
  id: "user-alice",
  displayName: "Alice",
  email: "alice@example.test",
  color: "#2563eb",
  role: "edit",
};

const bob: CollaborationActor = {
  id: "user-bob",
  displayName: "Bob",
  color: "#dc2626",
  role: "comment",
};

function makeClock(start = 1_000) {
  let value = start;
  return {
    now: () => value,
    advance: (ms: number) => {
      value += ms;
    },
  };
}

function makeSessionFactory(clock = makeClock()) {
  let nextSession = 0;
  return {
    clock,
    create: (savedMapId: string, actor: CollaborationActor, options: { peerTtlMs?: number; lockTtlMs?: number } = {}) =>
      createFixtureCollaborationSession(savedMapId, actor, {
        heartbeatMs: 100,
        peerTtlMs: options.peerTtlMs ?? 500,
        lockTtlMs: options.lockTtlMs ?? 400,
        now: clock.now,
        generateSessionId: () => `session-${++nextSession}`,
      }),
  };
}

function participants(session: CollaborationSession) {
  return Object.keys(session.getSnapshot().participants).sort();
}

function makeStorage(): Storage {
  const values = new Map<string, string>();
  return {
    get length() {
      return values.size;
    },
    clear: () => values.clear(),
    getItem: (key: string) => values.get(key) ?? null,
    key: (index: number) => Array.from(values.keys())[index] ?? null,
    removeItem: (key: string) => values.delete(key),
    setItem: (key: string, value: string) => {
      values.set(key, value);
    },
  };
}

describe("fixture collaboration session", () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.stubGlobal("BroadcastChannel", undefined);
    vi.stubGlobal("localStorage", makeStorage());
    localStorage.clear();
  });

  afterEach(() => {
    localStorage.clear();
    vi.unstubAllGlobals();
    vi.useRealTimers();
  });

  it("shares participants and cursors across same-map sessions through the fixture transport", () => {
    const factory = makeSessionFactory();
    const aliceSession = factory.create("map-1", alice);
    const bobSession = factory.create("map-1", bob);

    expect(participants(aliceSession)).toEqual(["user-alice", "user-bob"]);
    expect(participants(bobSession)).toEqual(["user-alice", "user-bob"]);

    bobSession.publishCursor({
      mapPoint: { lng: -157.8583, lat: 21.3069 },
      screenPoint: { x: 10, y: 20 },
    });

    expect(aliceSession.getSnapshot().cursors["user-bob"]).toMatchObject({
      participantId: "user-bob",
      mapPoint: { lng: -157.8583, lat: 21.3069 },
      screenPoint: { x: 10, y: 20 },
    });

    aliceSession.dispose();
    bobSession.dispose();
  });

  it("keeps map channels isolated", () => {
    const factory = makeSessionFactory();
    const aliceSession = factory.create("map-1", alice);
    const bobSession = factory.create("map-2", bob);

    expect(participants(aliceSession)).toEqual(["user-alice"]);
    expect(participants(bobSession)).toEqual(["user-bob"]);

    aliceSession.dispose();
    bobSession.dispose();
  });

  it("publishes feature selections and enforces unexpired edit locks", () => {
    const factory = makeSessionFactory();
    const aliceSession = factory.create("map-1", alice);
    const bobSession = factory.create("map-1", bob);
    const feature: FeatureRef = { layerId: "parcels", featureId: "42" };

    aliceSession.selectFeature(feature);
    expect(bobSession.getSnapshot().selections["parcels:42"]).toMatchObject({
      participantId: "user-alice",
      feature,
    });

    expect(aliceSession.claimFeature(feature)).toBe(true);
    expect(bobSession.claimFeature(feature)).toBe(false);

    aliceSession.releaseFeature(feature);
    expect(bobSession.claimFeature(feature)).toBe(true);
    expect(aliceSession.getSnapshot().editLocks["parcels:42"]).toMatchObject({
      participantId: "user-bob",
      feature,
    });

    aliceSession.dispose();
    bobSession.dispose();
  });

  it("prunes stale peers, cursors, selections, follows, and locks", () => {
    const factory = makeSessionFactory();
    const aliceSession = factory.create("map-1", alice, { peerTtlMs: 250, lockTtlMs: 150 });
    const bobSession = factory.create("map-1", bob, { peerTtlMs: 250, lockTtlMs: 150 });
    const feature: FeatureRef = { layerId: "roads", featureId: "7" };

    bobSession.publishCursor({ mapPoint: { lng: 1, lat: 2 } });
    bobSession.selectFeature(feature);
    bobSession.claimFeature(feature);
    aliceSession.follow("user-bob");

    expect(aliceSession.getSnapshot().followTarget).toEqual({ participantId: "user-bob", since: 1_000 });

    bobSession.dispose();
    factory.clock.advance(251);

    const snapshot = aliceSession.getSnapshot();
    expect(snapshot.participants["user-bob"]).toBeUndefined();
    expect(snapshot.cursors["user-bob"]).toBeUndefined();
    expect(snapshot.selections["roads:7"]).toBeUndefined();
    expect(snapshot.editLocks["roads:7"]).toBeUndefined();
    expect(snapshot.followTarget).toBeNull();

    aliceSession.dispose();
  });

  it("heartbeats refresh the local participant and notify subscribers", () => {
    const factory = makeSessionFactory();
    const session = factory.create("map-1", alice);
    const listener = vi.fn();
    session.subscribe(listener);

    factory.clock.advance(100);
    vi.advanceTimersByTime(100);

    expect(session.getSnapshot().participants["user-alice"]?.lastSeenAt).toBe(1_100);
    expect(listener).toHaveBeenCalled();

    session.dispose();
  });
});
