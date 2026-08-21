import { validatePinnedJsonSchema } from './json-schema-validator.mjs';

/**
 * The release and SDK paths are aliases for one consumer-owned aggregate.
 * Validate both write intents against that pinned schema, then deliberately
 * return the same immutable byte string for the two distinct files.
 */
export function buildReceiptAliasBytes(aggregate, receiptSchema) {
  validatePinnedJsonSchema(aggregate, receiptSchema);
  validatePinnedJsonSchema(aggregate, receiptSchema);
  const bytes = `${JSON.stringify(aggregate, null, 2)}\n`;
  return Object.freeze({ aggregateBytes: bytes, sdkBytes: bytes });
}
