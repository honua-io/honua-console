# ADR 0002: Realtime Studio and shared agent workspace

Status: Accepted for the flagged preview implementation.

Console embeds the realtime `<honua-studio-app>` custom element behind `Studio:EmbedJsStudio`, using pinned same-origin production assets. The existing non-realtime Blazor Studio builders remain shelved under their existing capability flag. This amends ADR 0001's framework direction while retaining one browser origin and one deployable Console artifact.

The browser uses its operator cookie and an antiforgery-protected, bounded BFF. Server bearer credentials stay on the host. Native chat uses one SDK session shared with the Studio element, with a separate agent credential and explicit server workspace membership. The human's browser authority is never used for the model's MCP calls. Missing bindings are visible unavailable states.

Server-owned per-view context, typed drafts, durable turns and canonical command receipts are authoritative. Presentation, editing, approval and execution remain separate actions. Prompt submission freezes the authorized context before subsequent navigation; dirty field patches and expected revisions preserve concurrent work. Signed model transcripts gate tool execution, and model text never establishes verified operation success.
