// Long enough for the browser to start reading the blob before its URL is revoked.
const REVOKE_DELAY_MS = 1000;

// Saves bytes the Console already fetched (with its own auth) as a browser download.
export function save(fileName, contentType, bytes) {
    const url = URL.createObjectURL(new Blob([bytes], { type: contentType }));
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    link.hidden = true;
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), REVOKE_DELAY_MS);
}
