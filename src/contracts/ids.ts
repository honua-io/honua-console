const CONTENT_ITEM_ID_ALPHABET = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

export const CONTENT_ITEM_ID_PATTERN = /^[0-9A-HJKMNP-TV-Z]{26}$/;

export function isContentItemId(value: unknown): value is string {
  return typeof value === "string" && CONTENT_ITEM_ID_PATTERN.test(value);
}

export function assertContentItemId(value: unknown, field = "id"): string {
  if (isContentItemId(value)) return value;
  throw new Error(`${field} must be a 26-character Crockford base32 ULID`);
}

export function createContentItemIdGenerator(): () => string {
  return () => `${encodeBase32(Date.now(), 10)}${randomBase32(16)}`;
}

export function createDeterministicContentItemIdGenerator(namespace = "id"): () => string {
  let counter = 0;
  const namespacePart = encodeBase32(hashNamespace(namespace), 8);
  return () => {
    counter += 1;
    return `01HXY3ZK7N${namespacePart}${encodeBase32(counter, 8)}`;
  };
}

function randomBase32(length: number): string {
  let out = "";
  for (let i = 0; i < length; i += 1) {
    out += CONTENT_ITEM_ID_ALPHABET[Math.floor(Math.random() * CONTENT_ITEM_ID_ALPHABET.length)];
  }
  return out;
}

function encodeBase32(value: number, width: number): string {
  let remaining = Math.max(0, Math.trunc(value));
  let out = "";
  for (let i = 0; i < width; i += 1) {
    out = CONTENT_ITEM_ID_ALPHABET[remaining % CONTENT_ITEM_ID_ALPHABET.length] + out;
    remaining = Math.floor(remaining / CONTENT_ITEM_ID_ALPHABET.length);
  }
  return out;
}

function hashNamespace(namespace: string): number {
  let hash = 2166136261;
  for (let i = 0; i < namespace.length; i += 1) {
    hash ^= namespace.charCodeAt(i);
    hash = Math.imul(hash, 16777619);
  }
  return hash >>> 0;
}
