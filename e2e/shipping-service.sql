BEGIN;
-- The tree browses the secure connection registry, not metadata snapshot connections.
-- Reuse the disposable quickstart's existing environment reference; never copy credentials.
INSERT INTO honua.data_connections(connection_id,name,host,port,database_name,username,
    provider_name,ssl_required,ssl_mode,secret_ref,secret_type)
VALUES ('94680100-0000-4000-8000-000000000001','shipping-folder-source','postgres',5432,
    'honua_dev','honua_user','postgis',false,'prefer',
    'env:ConnectionStrings__DefaultConnection','env');
INSERT INTO honua.services(service_name) VALUES ('public-endpoint-check'), ('folder-navigation-check');
CREATE TABLE honua_data.endpoint_check (
    objectid SERIAL PRIMARY KEY,
    geometry geometry(Point,4326),
    attributes jsonb
);
INSERT INTO honua_data.endpoint_check(geometry, attributes)
VALUES (ST_SetSRID(ST_Point(-122,47),4326), '{}');
INSERT INTO honua.layers(layer_id,layer_name,table_schema,table_name,geometry_type,srid,extent,storage_options)
VALUES (946800,'Endpoint check','honua_data','endpoint_check','Point',4326,
    ST_MakeEnvelope(-123,46,-121,48,4326),
    '{"schemaName":"honua_data","tableName":"endpoint_check","primaryKeyColumn":"objectid","geometryColumn":"geometry","attributesColumn":"attributes"}'::jsonb);
INSERT INTO honua.service_layers(service_name,layer_id,layer_order)
VALUES ('public-endpoint-check',946800,0);
INSERT INTO honua.layer_fields(layer_id,field_name,field_type,field_order,nullable)
VALUES (946800,'objectid','Integer',0,false),(946800,'geometry','Geometry',1,true);
INSERT INTO honua.layers(layer_id,layer_name,table_schema,table_name,geometry_type,srid,extent,storage_options)
SELECT 946801,'Folder navigation layer',table_schema,table_name,geometry_type,srid,extent,storage_options
FROM honua.layers WHERE layer_id = 946800;
UPDATE honua.services SET connection_id = '94680100-0000-4000-8000-000000000001'
WHERE service_name = 'folder-navigation-check';
INSERT INTO honua.service_layers(service_name,layer_id,layer_order)
VALUES ('folder-navigation-check',946801,0);
INSERT INTO honua.layer_fields(layer_id,field_name,field_type,field_order,nullable)
SELECT 946801,field_name,field_type,field_order,nullable FROM honua.layer_fields WHERE layer_id = 946800;
COMMIT;
