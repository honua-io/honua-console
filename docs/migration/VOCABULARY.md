# Console vocabulary transition

The Console uses the [Honua vocabulary](https://github.com/honua-io/honua-server/blob/trunk/docs/concepts/domain/README.md). The data menu names Datasets, Layers, Services, and Connections. Environments select the servers managed by Console. Access uses Users and Roles. Folders group Services; they do not grant access.

The Services menu opens `/operate/services`, a service list with protocol, layer count, and runtime status. Each service links to its settings. Layers opens `/operate/layers`. Datasets opens `/operate/data`. Existing deep links retain their routes during this presentation change.

Environment forms no longer offer tenant selection. Saved isolation identifiers and server authorization checks remain intact. This changes presentation, not the authentication boundary.

Publishing labels use Dataset, Layer, Service, metadata, and released content. Existing SDK types, serialized fields, and server routes remain at the current published contract until the coordinated server migration and SDK releases. Dataset kind filters and canonical kind details, object-search aliases, typed Studio kinds, and the remaining model names follow that contract boundary. Source-system import terms remain source terms.

Review additions with the [shared retired-terms checker and policy](https://github.com/honua-io/honua-server/tree/115011ce760443e93ec70e4bd5123d7b9fa08414/scripts/ci). Use the checker and adjacent policy from that same pinned server commit; do not create a second retired-terms list. Contextual UI words are also covered by the mandatory vocabulary review rule in AGENTS.md.
