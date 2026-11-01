---
type: guide
title: "Use the built-in Studio assistant"
---
# Use the built-in Studio assistant

On a standard Server installation, **Start conversation** checks the configured Studio AI provider and starts the browser's existing Studio SDK session. Model requests use `POST /api/v1/studio/ai/chat`; draft creation and edits use the canonical MCP tools at `/mcp`. There is no server workspace-creation call in this mode. The selected environment's current human bearer stays in the Console BFF. Model and AWS credentials stay on Server.

Enable `Studio:EndUserAuthorization:Enabled` and configure `StudioAiProxy` on Server. Bedrock also requires an exact model-disclosure approval and initialized, resumed AI invocation admission. Follow Server's **Studio assistant on Bedrock** guide and opt-in Compose overlay. Enabling the proxy alone does not initialize the admission ledger. Disabled or unavailable configuration is displayed before a conversation starts; canonical admission errors remain visible when sending.

Open a map or dashboard in Studio, start the conversation, and send your request. The assistant uses the selected draft and its current generation. Save or discard unsaved edits before submission. Navigation during a running turn does not retarget that turn. Canonical tools enforce permissions, ownership and generation preconditions. Publication still uses the separate human approval flow.

The built-in transcript remains in memory in this browser tab across Console navigation. Reloading or closing the tab ends the conversation. Drafts and saved versions remain on Server; save your work through Studio. Model prose does not establish that a mutation succeeded.

A deployment with the separate, matching agent workspace API can explicitly set `Honua__Agent__WorkspaceApiEnabled=true` on Console to retain the shared external-worker workspace integration described in [agent-workspace.md](agent-workspace.md). That contract is absent from standard Server trunk. It is not enabled by setting a Bedrock provider, `HONUA_AGENT_API_KEY`, or `HONUA_NATIVE_MODEL_ENABLED`.

A Console rebuild is required to adopt this transport correction. Server configuration alone cannot repair an older Console image that calls the absent workspace API.
