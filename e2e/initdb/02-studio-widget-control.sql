-- Typed source for the independent widget transport control. Customer imports
-- use uploaded files and never use this seeded table as import evidence.
CREATE TABLE IF NOT EXISTS public.e2e_customer_regions (
  id integer PRIMARY KEY,
  name text NOT NULL,
  category text NOT NULL,
  population integer NOT NULL,
  rating double precision NOT NULL,
  geom geometry(Polygon, 4326) NOT NULL
);
INSERT INTO public.e2e_customer_regions VALUES
  (1, 'West', 'west', 10, 1.5, ST_MakeEnvelope(-2, -1, -0.2, 1, 4326)),
  (2, 'East', 'east', 20, 2.5, ST_MakeEnvelope(0.2, -1, 2, 1, 4326))
ON CONFLICT (id) DO NOTHING;
