# Console vocabulary release notes

Console navigation now uses Datasets, Users, and Roles. A database or file remains a Connection; the server managed by Console is an Environment. Data and publishing controls name Dataset, Layer, and Service instead of retired object names. Environment setup no longer exposes tenant selection.

Data > Services now opens its own service list. Data > Layers opens the layer list. Existing route paths, published SDK dependencies, and serialized contracts are unchanged by this batch. No database migration is required for this Console presentation change.

Console navigation menus stay open while pointer focus moves between their links, so Services and Layers receive the click before the menu closes. Delayed initial page-heading focus preserves a menu the user has already opened after hydration; subsequent route changes still focus the new page heading.

The [transition guide](VOCABULARY.md) records the remaining coordinated model, SDK, kind-filter, and search-alias work. The Catalog administration feature keeps its separate capability gate.

Forms now use the canonical `/admin/forms` and `/forms` server routes, with `formVersion` route parameters. Deploy the matching server migration first. The builder shows Released and Go live. Form presentation models use `IsReleased`, `HonuaFormStatuses.Released`, `ReleasedVersion`, `StudioFormGoLiveReadiness`, `StudioFormGoLiveEvaluator`, and `CanGoLive`; the transition guide lists the replaced names. Retired route and model aliases are not supplied. The serialized `published` status and `/publish` action remain unchanged.

Catalog discovery uses `environmentId`/`environmentName` in registry responses and `Console:CatalogDiscovery:Environments` in server fixture configuration. Console links use `?environment=<id>`; update saved discovery links that use the retired `workspace` query. Environment identifier values and item handles are unchanged. Deploy with a server that supplies the canonical Environment contract.

Access role and membership responses use `environmentId`/`environmentName`, and Roles/Users links use `?environment=<id>`. Update saved access links that use `workspace`. Access scopes are Environment, Content, Layer, and Dataset field. Environment and role identifier values, API URL values, and audit operation names remain unchanged. Deploy the matching server ownership migration and canonical contract together with this client; retired field and scope aliases are not supplied.
