-- Source table for data-flow-go-live.live.spec.ts. honua-server allows one source-backed layer per
-- table, so the Go live journey needs a table no other spec publishes.

CREATE TABLE IF NOT EXISTS public.e2e_golive_src (
  id   integer PRIMARY KEY,
  name text    NOT NULL,
  geom geometry(Polygon, 3857) NOT NULL
);

INSERT INTO public.e2e_golive_src (id, name, geom)
VALUES
  (1, 'north', ST_SetSRID(ST_MakeEnvelope(   0,   0,  100,  100, 3857), 3857)),
  (2, 'south', ST_SetSRID(ST_MakeEnvelope( 200, 200,  300,  300, 3857), 3857)),
  (3, 'east',  ST_SetSRID(ST_MakeEnvelope( 400, 400,  500,  500, 3857), 3857))
ON CONFLICT (id) DO NOTHING;
