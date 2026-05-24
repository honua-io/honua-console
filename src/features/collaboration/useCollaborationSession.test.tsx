import { renderHook, waitFor } from "@testing-library/react";
import { createFixtureSavedMapCollaborationTransport } from "@honua/sdk-js/collaboration";
import { afterEach, describe, expect, it } from "vitest";

import type {
  HonuaSavedMapCollaborationClientOptions,
  SavedMapCollaborationJoinRequest,
} from "../../sdk/collaboration";
import { addConsoleSmokeListener, type ConsoleSmokeEvent } from "../../telemetry/smoke";
import { useCollaborationSession } from "./useCollaborationSession";

interface HookProps {
  readonly options: HonuaSavedMapCollaborationClientOptions | undefined;
  readonly join: SavedMapCollaborationJoinRequest | undefined;
}

const cleanups: Array<() => void> = [];

afterEach(() => {
  while (cleanups.length) cleanups.pop()?.();
});

describe("useCollaborationSession", () => {
  it("resets to pending-binding and emits smoke when collaboration inputs disappear", async () => {
    const options: HonuaSavedMapCollaborationClientOptions = {
      transport: createFixtureSavedMapCollaborationTransport(),
    };
    const join: SavedMapCollaborationJoinRequest = {
      mapId: "map-1",
      participantId: "participant-1",
    };

    const events: ConsoleSmokeEvent[] = [];
    cleanups.push(addConsoleSmokeListener((event) => events.push(event)));

    const initialProps: HookProps = { options, join };
    const { result, rerender } = renderHook(
      ({ options: nextOptions, join: nextJoin }: HookProps) =>
        useCollaborationSession(nextOptions, nextJoin),
      { initialProps },
    );

    await waitFor(() => {
      expect(result.current.status).toBe("ok");
    });

    rerender({ options: undefined, join: undefined });

    await waitFor(() => {
      expect(result.current.status).toBe("pending-binding");
    });
    expect(events).toEqual(
      expect.arrayContaining([
        expect.objectContaining({
          surface: "collaboration.session.join",
          sdkSubpath: "collaboration",
          status: "pending-binding",
          detail: expect.objectContaining({ waitingFor: ["collaboration-transport"] }),
        }),
      ]),
    );
  });
});
