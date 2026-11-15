BEGIN;
UPDATE honua.metadata_v2_snapshots
SET document = jsonb_set(jsonb_set(document, '{folders}',
    '[{"metadata":{"id":"shipping-folder","name":"ShippingFolder","title":"Shipping Folder"},"previousNames":[],"deleted":false}]'::jsonb),
    '{services}', (SELECT jsonb_agg(CASE
        WHEN service->'metadata'->>'name' = 'folder-navigation-check'
        THEN service || '{"folderId":"shipping-folder"}'::jsonb
        ELSE service END ORDER BY service->'metadata'->>'id')
        FROM jsonb_array_elements(document->'services') AS service));
UPDATE honua.metadata_v2_snapshots SET etag = '"' || md5(document::text) || '"';
UPDATE honua.metadata_v2_current AS current
SET etag = snapshot.etag
FROM honua.metadata_v2_snapshots AS snapshot
WHERE current.environment = snapshot.environment AND current.revision = snapshot.revision;
COMMIT;
