-- e2e testbed seed
-- Runs on first container start (postgres init hooks); idempotent via ON CONFLICT.
-- Provides the source table that services-layers.live.spec.ts publishes as a service.

CREATE EXTENSION IF NOT EXISTS postgis;

-- Source table: 3 polygon features in EPSG:3857, used by services-layers and studio-results.
CREATE TABLE IF NOT EXISTS public.e2e_layer_src (
  id   integer PRIMARY KEY,
  name text    NOT NULL,
  geom geometry(Polygon, 3857) NOT NULL
);

INSERT INTO public.e2e_layer_src (id, name, geom)
VALUES
  (1, 'alpha', ST_SetSRID(ST_MakeEnvelope(   0,   0,  100,  100, 3857), 3857)),
  (2, 'beta',  ST_SetSRID(ST_MakeEnvelope( 200, 200,  300,  300, 3857), 3857)),
  (3, 'gamma', ST_SetSRID(ST_MakeEnvelope( 400, 400,  500,  500, 3857), 3857))
ON CONFLICT (id) DO NOTHING;
