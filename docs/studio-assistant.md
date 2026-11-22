---
type: guide
title: "Studio assistant"
---
# Studio assistant

**The shipped static Console does not offer the assistant.** Its Conversation
drawer shows "Conversation is unavailable in this Console deployment" and has
no **Start conversation** button. Studio's map and app editors work without
it. To have an agent build or change content, connect your own agent to the
server's MCP endpoint (`/mcp`); drafts it creates appear in Studio, and
publication still goes through the separate human approval flow.

## What the assistant needs on Server

The assistant's model requests go to `POST /api/v1/studio/ai/chat`, and draft
changes use the canonical MCP tools at `/mcp`. Model and AWS credentials stay
on Server. Server needs `Studio:EndUserAuthorization:Enabled` and a configured
`StudioAiProxy`. Bedrock also requires an exact model-disclosure approval and
initialized, resumed AI invocation admission; enabling the proxy alone does not
initialize the admission ledger. Follow Server's **Studio assistant on
Bedrock** guide and its opt-in Compose overlay.

## Test host only

`tests/Honua.Console.LegacyHost` can still mount the drawer for tests, and with
`Honua__Agent__WorkspaceApiEnabled=true` it uses the external-worker workspace
integration described in [agent-workspace.md](agent-workspace.md). Neither
setting reaches a deployed Console: the static Console reads only its
`config.json`, which rejects unknown keys, and it always loads the bundled
Studio editor. Setting a Bedrock provider, `HONUA_AGENT_API_KEY` or
`HONUA_NATIVE_MODEL_ENABLED` does not enable the assistant either.
