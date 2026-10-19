# ADR 0002: Realtime Studio and shared agent workspace

Status: Accepted for the flagged preview implementation.

Console embeds the realtime `<honua-studio-app>` custom element behind `Studio:EmbedJsStudio`, using pinned same-origin production assets. Legacy standalone builders are retired. Their old URLs provide guidance to the realtime workspace, and capability flags cannot restore the old product. `honua-studio` remains the active editor bundle dependency; no repository archive or migration is implied. This amends ADR 0001's framework direction while retaining one browser origin and one deployable Console artifact.

The browser uses its operator cookie and an antiforgery-protected, bounded BFF. Server bearer credentials stay on the host. Native chat uses one SDK session shared with the Studio element, with a separate agent credential and explicit server workspace membership. The human's browser authority is never used for the model's MCP calls. Missing bindings are visible unavailable states.

Server-owned per-view context, typed drafts, durable turns and canonical command receipts are authoritative. Presentation, editing, approval and execution remain separate actions. Prompt submission freezes the authorized context before subsequent navigation; dirty field patches and expected revisions preserve concurrent work. Signed model transcripts gate tool execution, and model text never establishes verified operation success.

The approved UX-001–007 implementation keeps one primary host conversation beside Console/Studio/Processes views. Embedded Studio suppresses its standalone composer when supplied the host adapter; standalone Studio retains chat. Home leads with local-agent Docker handoff, existing-server connection and continuation. Add Data uses contextual forms and the shared conversation, rather than a separate AI/Manual generator loop. Resource failures cannot masquerade as empty collections, and mobile conversation/navigation remain in document flow. Existing routes, frozen context, dirty-edit guards and separate human approval remain authoritative. [Interaction model](../design/unified-workspace-ux-implementation.md).

External agents receive an explicit refs-only tab context (`workspaceId`, `viewId`, `contextRevision`, `targetId`). The current server target and original revision must match; no latest-view guessing or silent target refresh is permitted. Per-turn catalog rediscovery keeps one SDK conversation while rechecking actual advertised metadata and private capability bindings.

Map-local styling uses the canonical map draft. Shared catalog styling is a distinct typed `shared-style` context with version and reviewed-impact pins. Shared mutations require complete authorized dependent map/app impact and optimistic concurrency. The advanced JSON CAS surface is not proof that the shared visual editor, label/assets, publication propagation or end-to-end workflow is qualified.
