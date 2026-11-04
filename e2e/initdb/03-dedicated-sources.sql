-- Dedicated source tables for live specs that seed their own layer. honua-server backs a source table
-- with one layer only, so each such spec needs a table no other spec publishes.
--   e2e_golive_src   -> data-flow-go-live.live.spec.ts
--   e2e_settings_src -> service-settings.live.spec.ts
--   e2e_types_src    -> service-types.live.spec.ts

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

CREATE TABLE IF NOT EXISTS public.e2e_settings_src (LIKE public.e2e_golive_src INCLUDING ALL);

INSERT INTO public.e2e_settings_src SELECT * FROM public.e2e_golive_src ON CONFLICT (id) DO NOTHING;

CREATE TABLE IF NOT EXISTS public.e2e_types_src (LIKE public.e2e_golive_src INCLUDING ALL);

INSERT INTO public.e2e_types_src SELECT * FROM public.e2e_golive_src ON CONFLICT (id) DO NOTHING;
