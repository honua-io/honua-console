const ANNOTATION_KEYS = new Set(['$schema', '$id', '$defs', 'title', 'description', 'default', 'examples']);
const VALIDATION_KEYS = new Set([
  '$ref', 'type', 'const', 'required', 'properties', 'additionalProperties', 'items',
  'minItems', 'minimum', 'minLength', 'format', 'pattern',
]);

/**
 * Validate a value against the pinned SDK receipt schema without importing a
 * second schema implementation into Console. Unsupported schema keywords fail
 * closed so a future SDK contract cannot silently bypass this gate.
 */
export function validatePinnedJsonSchema(value, schema) {
  if (!schema || typeof schema !== 'object' || Array.isArray(schema)) {
    throw new Error('pinned Console receipt schema must be a JSON object');
  }
  assertSupportedSchema(schema, '#');
  validate(value, schema, schema, '$');
}

function validate(value, schema, root, path) {
  if (schema.$ref !== undefined) {
    const resolved = resolveLocalReference(root, schema.$ref);
    validate(value, resolved, root, path);
  }
  if (schema.const !== undefined && !deepEqual(value, schema.const)) {
    throw new Error(`${path} must equal ${JSON.stringify(schema.const)}`);
  }
  if (schema.type !== undefined) assertType(value, schema.type, path);

  if (schema.type === 'object' || schema.properties || schema.required) {
    if (!isRecord(value)) throw new Error(`${path} must be an object`);
    const properties = schema.properties ?? {};
    for (const name of schema.required ?? []) {
      if (!Object.hasOwn(value, name)) throw new Error(`${path}.${name} is required`);
    }
    for (const [name, child] of Object.entries(value)) {
      if (Object.hasOwn(properties, name)) validate(child, properties[name], root, `${path}.${name}`);
      else if (schema.additionalProperties === false) throw new Error(`${path}.${name} is not allowed`);
      else if (isRecord(schema.additionalProperties)) validate(child, schema.additionalProperties, root, `${path}.${name}`);
    }
  }

  if (Array.isArray(value)) {
    if (schema.minItems !== undefined && value.length < schema.minItems) {
      throw new Error(`${path} must contain at least ${schema.minItems} items`);
    }
    if (schema.items) value.forEach((child, index) => validate(child, schema.items, root, `${path}[${index}]`));
  }
  if (typeof value === 'string') {
    if (schema.minLength !== undefined && value.length < schema.minLength) {
      throw new Error(`${path} must contain at least ${schema.minLength} characters`);
    }
    if (schema.pattern !== undefined && !(new RegExp(schema.pattern, 'u')).test(value)) {
      throw new Error(`${path} does not match ${schema.pattern}`);
    }
    if (schema.format === 'uuid' && !/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(value)) {
      throw new Error(`${path} must be a UUID`);
    }
    if (schema.format === 'uri') {
      try { new URL(value); }
      catch { throw new Error(`${path} must be an absolute URI`); }
    }
  }
  if (typeof value === 'number' && schema.minimum !== undefined && value < schema.minimum) {
    throw new Error(`${path} must be at least ${schema.minimum}`);
  }
}

function assertSupportedSchema(schema, path) {
  if (!isRecord(schema)) throw new Error(`${path} schema node must be an object`);
  for (const key of Object.keys(schema)) {
    if (!ANNOTATION_KEYS.has(key) && !VALIDATION_KEYS.has(key)) {
      throw new Error(`pinned Console receipt schema uses unsupported keyword ${path}/${key}`);
    }
  }
  for (const [name, child] of Object.entries(schema.$defs ?? {})) assertSupportedSchema(child, `${path}/$defs/${name}`);
  for (const [name, child] of Object.entries(schema.properties ?? {})) assertSupportedSchema(child, `${path}/properties/${name}`);
  if (isRecord(schema.items)) assertSupportedSchema(schema.items, `${path}/items`);
  if (isRecord(schema.additionalProperties)) assertSupportedSchema(schema.additionalProperties, `${path}/additionalProperties`);
  if (schema.format !== undefined && !['uuid', 'uri'].includes(schema.format)) {
    throw new Error(`pinned Console receipt schema uses unsupported format ${schema.format}`);
  }
}

function resolveLocalReference(root, reference) {
  if (typeof reference !== 'string' || !reference.startsWith('#/')) {
    throw new Error(`pinned Console receipt schema uses unsupported reference ${String(reference)}`);
  }
  const resolved = reference.slice(2).split('/').reduce((current, segment) => {
    const key = segment.replaceAll('~1', '/').replaceAll('~0', '~');
    return current?.[key];
  }, root);
  if (!isRecord(resolved)) throw new Error(`pinned Console receipt schema reference does not resolve: ${reference}`);
  return resolved;
}

function assertType(value, type, path) {
  const matches = type === 'object' ? isRecord(value)
    : type === 'array' ? Array.isArray(value)
      : type === 'integer' ? Number.isInteger(value)
        : type === 'number' ? typeof value === 'number' && Number.isFinite(value)
          : type === 'string' ? typeof value === 'string'
            : type === 'boolean' ? typeof value === 'boolean'
              : false;
  if (!matches) throw new Error(`${path} must be ${type}`);
}

function isRecord(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function deepEqual(left, right) {
  return JSON.stringify(left) === JSON.stringify(right);
}
