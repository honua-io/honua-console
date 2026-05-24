// @ts-check
import tsParser from "@typescript-eslint/parser";
import tsPlugin from "@typescript-eslint/eslint-plugin";
import reactHooks from "eslint-plugin-react-hooks";

// Explicit allowlist of protocol DTO names the SDK owns. Per design Q5 we
// chose the narrow, explicit form to avoid catching unrelated declarations
// (e.g. `ProvenanceLoader` is a Console-local view-model). Add a name here
// when a new SDK contract lands so Console can't fork it.
const FORBIDDEN_DTO_NAMES = [
  "ContentItem",
  "ContentItemSummary",
  "ContentOwner",
  "ContentAccess",
  "SavedMapItem",
  "SavedMapSummary",
  "MetadataV2",
  "MetadataV2Record",
  "ProvenanceRecord",
  "ProvenanceTrail",
  "SharePolicy",
  "SharePolicyRequest",
  "ShareRequest",
  "ShareResponse",
  "EmbedRequest",
  "EmbedResponse",
  "MapPackageBinding",
  "AppPackage",
  "DashboardPackage",
  "ReportPackage",
  "CapabilityBundle",
  "EntitlementFlag",
];

const dtoNameAlt = FORBIDDEN_DTO_NAMES.join("|");
const DTO_NAME_REGEX = `^(${dtoNameAlt})$`;

const NO_RESTRICTED_DTO_SELECTORS = [
  {
    selector: `TSInterfaceDeclaration[id.name=/${DTO_NAME_REGEX}/]`,
    message:
      "Protocol DTOs may only be declared in src/sdk/** (re-exports from @honua/sdk-js). " +
      "If you need a view-model type, give it a feature-specific name (e.g. CatalogItemViewModel).",
  },
  {
    selector: `TSTypeAliasDeclaration[id.name=/${DTO_NAME_REGEX}/]`,
    message:
      "Protocol DTOs may only be declared in src/sdk/** (re-exports from @honua/sdk-js). " +
      "If you need a view-model type, give it a feature-specific name (e.g. CatalogItemViewModel).",
  },
];

export default [
  {
    ignores: ["dist", "node_modules", ".vite", "coverage"],
  },
  {
    files: ["src/**/*.{ts,tsx}"],
    languageOptions: {
      parser: tsParser,
      parserOptions: {
        ecmaVersion: "latest",
        sourceType: "module",
        ecmaFeatures: { jsx: true },
      },
    },
    plugins: {
      "@typescript-eslint": tsPlugin,
      "react-hooks": reactHooks,
    },
    rules: {
      // SDK-barrel-only import guard. Anything outside `src/sdk/**` must use
      // the per-area re-exports in `src/sdk/...`.
      "no-restricted-imports": [
        "error",
        {
          patterns: [
            {
              group: ["@honua/sdk-js", "@honua/sdk-js/*"],
              message:
                "Import from src/sdk/<area> instead. Only src/sdk/** is allowed to import @honua/sdk-js directly.",
            },
          ],
        },
      ],
      // No local DTO redefinitions for the names the SDK owns.
      "no-restricted-syntax": ["error", ...NO_RESTRICTED_DTO_SELECTORS],
      "react-hooks/rules-of-hooks": "error",
      "react-hooks/exhaustive-deps": "warn",
    },
  },
  {
    // Inside the SDK barrel, both guards are turned off so re-exports work.
    files: ["src/sdk/**/*.{ts,tsx}"],
    languageOptions: {
      parser: tsParser,
      parserOptions: {
        ecmaVersion: "latest",
        sourceType: "module",
      },
    },
    plugins: {
      "@typescript-eslint": tsPlugin,
    },
    rules: {
      "no-restricted-imports": "off",
      "no-restricted-syntax": "off",
    },
  },
  {
    // Tests use jsdom; allow direct SDK imports for setup, but keep the DTO
    // rule on so tests can't sneak in a duplicate type.
    files: ["tests/**/*.{ts,tsx}", "src/**/*.test.{ts,tsx}"],
    languageOptions: {
      parser: tsParser,
      parserOptions: {
        ecmaVersion: "latest",
        sourceType: "module",
        ecmaFeatures: { jsx: true },
      },
    },
    plugins: {
      "@typescript-eslint": tsPlugin,
    },
    rules: {
      "no-restricted-imports": "off",
      "no-restricted-syntax": ["error", ...NO_RESTRICTED_DTO_SELECTORS],
    },
  },
];
