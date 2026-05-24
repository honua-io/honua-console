import { renderHook, waitFor } from "@testing-library/react";
import { createFixtureSavedMapCollaborationTransport } from "@honua/sdk-js/collaboration";
import { describe, expect, it } from "vitest";

import type {
  HonuaSavedMapCollaborationClientOptions,
  SavedMapCollaborationJoinRequest,
} from "../../sdk/collaboration";
import { useCollaborationSession } from "./useCollaborationSession";

interface HookProps {
  readonly options: HonuaSavedMapCollaborationClientOptions | undefined;
  readonly join: SavedMapCollaborationJoinRequest | undefined;
}

describe("useCollaborationSession", () => {
  it("resets to pending-binding when collaboration inputs disappear", async () => {
    const options: HonuaSavedMapCollaborationClientOptions = {
      transport: createFixtureSavedMapCollaborationTransport(),
    };
    const join: SavedMapCollaborationJoinRequest = {
      mapId: "map-1",
      participantId: "participant-1",
    };

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
  });
});
