# Legacy host contract fixture

Test-only host preserving the proxy and authentication contracts while their replacements move to honua-server. This project is never released. Only the legacy customer regression packages it into a test container. The shipping Web project is standalone Blazor WebAssembly. Remove this fixture after the replacement API endpoint journeys pass.

The legacy Playwright configurations launch this fixture and keep their existing assertions. They are regression evidence only; `playwright.static.config.ts` qualifies the published browser bundle. Fetch the locked Studio release before building this fixture. Its static asset manifest links the browser source assets without creating a second shipping product.
