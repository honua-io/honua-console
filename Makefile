# Honua Console — developer targets
#
# One-command live e2e:
#   make e2e-live
#   npm run e2e:live   (equivalent)
#
# See e2e/README.md for details.

.PHONY: e2e-live

## e2e-live: Boot the Docker stack (PostGIS + Redis + honua-server), run live Playwright specs, tear down.
e2e-live:
	node e2e/run-live.mjs
