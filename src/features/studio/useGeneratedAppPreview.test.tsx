import { renderHook, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import type {
  HonuaGeneratedAppLoadOptions,
  HonuaGeneratedAppPreviewInput,
  HonuaGeneratedAppPreviewResult,
} from "../../sdk/generated-app";
import { addConsoleSmokeListener } from "../../telemetry/smoke";
import { useGeneratedAppPreview } from "./useGeneratedAppPreview";

vi.mock("../../sdk/generated-app", () => ({
  previewGeneratedApp: vi.fn(
    async (): Promise<HonuaGeneratedAppPreviewResult> => ({
      status: "error",
      errors: [],
    }),
  ),
}));

interface HookProps {
  readonly input: HonuaGeneratedAppPreviewInput | undefined;
  readonly options: HonuaGeneratedAppLoadOptions | undefined;
}

const cleanups: Array<() => void> = [];

afterEach(() => {
  while (cleanups.length) cleanups.pop()?.();
});

describe("useGeneratedAppPreview", () => {
  it("resets to pending-binding when preview inputs disappear", async () => {
    const input = {} as HonuaGeneratedAppPreviewInput;
    const options = {} as HonuaGeneratedAppLoadOptions;

    const initialProps: HookProps = { input, options };
    const { result, rerender } = renderHook(
      ({ input: nextInput, options: nextOptions }: HookProps) =>
        useGeneratedAppPreview(nextInput, nextOptions),
      { initialProps },
    );

    await waitFor(() => {
      expect(result.current.status).toBe("ok");
    });

    rerender({ input: undefined, options: undefined });

    await waitFor(() => {
      expect(result.current.status).toBe("pending-binding");
    });
  });

  it("emits pending-binding smoke when preview inputs are absent", async () => {
    const events: unknown[] = [];
    cleanups.push(addConsoleSmokeListener((event) => events.push(event)));

    renderHook(() => useGeneratedAppPreview(undefined, undefined));

    await waitFor(() => {
      expect(events).toEqual([
        expect.objectContaining({
          surface: "studio.generated-app.preview",
          sdkSubpath: "generated-app",
          status: "pending-binding",
          detail: expect.objectContaining({ waitingFor: ["generated-app.preview"] }),
        }),
      ]);
    });
  });
});
