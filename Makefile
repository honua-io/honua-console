# Honua Console — developer targets
#
# Live e2e (first set HONUA_CONSOLE_E2E_SERVER_IMAGE to the selected digest):
#   make e2e-live
#   npm run e2e:live   (equivalent)
#
# See e2e/README.md for details.

.PHONY: e2e-live

## e2e-live: Boot the Docker stack (PostGIS + Redis + honua-server), run live Playwright specs, tear down.
e2e-live:
	node e2e/run-live.mjs
