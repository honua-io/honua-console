// Workflow package IDs are opaque UTF-16 store keys, matching the server task validator.
const invalidPackageId = /\p{Cc}|^[\p{Zs}\u2028\u2029]|[\p{Zs}\u2028\u2029]$/u;
export function processTask(value) {
    if (!value || typeof value.packageId !== 'string' || value.packageId.length < 1 || value.packageId.length > 256
        || invalidPackageId.test(value.packageId) || !Number.isInteger(value.version) || value.version < 1 || value.version > 2147483647
        || !/^[a-f0-9]{64}$/.test(value.contentHash || ''))
        throw new Error('Processes returned no verified immutable package identity.');
    return {kind:'workflow-package',id:value.packageId,revision:value.version,contentHash:value.contentHash};
}
