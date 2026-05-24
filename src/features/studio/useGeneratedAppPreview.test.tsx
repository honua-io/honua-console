import { renderHook, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import type {
  HonuaGeneratedAppLoadOptions,
  HonuaGeneratedAppPreviewInput,
  HonuaGeneratedAppPreviewResult,
} from "../../sdk/generated-app";
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
});
