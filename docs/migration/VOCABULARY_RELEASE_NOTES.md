# Console vocabulary release notes

Console navigation now uses Datasets, Users, and Roles. A database or file remains a Connection; the server managed by Console is an Environment. Data and publishing controls name Dataset, Layer, and Service instead of retired object names. Environment setup no longer exposes tenant selection.

Data > Services now opens its own service list. Data > Layers opens the layer list. Existing route paths, published SDK dependencies, and serialized contracts are unchanged by this batch. No database migration is required for this Console presentation change.

Data menu links stay open while pointer focus moves between destinations, so Services and Layers receive the click before the menu closes.

The [transition guide](VOCABULARY.md) records the remaining coordinated model, SDK, kind-filter, and search-alias work. The Catalog administration feature keeps its separate capability gate.
