import { HonuaClient, type HonuaClientOptions } from "@honua/sdk-js/honua";

import type { Session } from "../auth/types.js";

export interface CreatePortalViewerSdkClientOptions
  extends Pick<HonuaClientOptions, "fetchFn" | "interceptors" | "retry" | "timeoutMs"> {
  baseUrl: string;
  session?: Session;
}

export function createPortalViewerSdkClient(options: CreatePortalViewerSdkClientOptions): HonuaClient {
  const bearerToken = options.session?.status === "authenticated" ? options.session.accessToken : undefined;

  return new HonuaClient({
    baseUrl: options.baseUrl,
    fetchFn: options.fetchFn,
    interceptors: options.interceptors,
    retry: options.retry,
    timeoutMs: options.timeoutMs,
    auth: bearerToken ? () => bearerToken : undefined,
  });
}
