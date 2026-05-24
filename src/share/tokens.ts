export function defaultPublicLinkToken(itemId: string): string {
  return `fixture-${itemId.slice(-8).toLowerCase()}`;
}

export function defaultEmbedToken(itemId: string): string {
  return `fixture-embed-${itemId.slice(-8).toLowerCase()}`;
}
