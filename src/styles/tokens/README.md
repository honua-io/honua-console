# Design Tokens

This directory is the future home for split design-token files (e.g. `colors.css`, `type.css`, `motion.css`).

For now, all tokens live in `../global.css` as CSS custom properties. Splitting happens when honua-console#4/#5 starts pulling tokens into Catalog/Viewer/Studio surfaces; until then, adding a single token to `global.css` is intentional and avoids premature reorganization.

When a real token source-of-truth lands (Style Dictionary, Figma export, etc.), it slots into this directory without needing to reorganise the styles tree.
