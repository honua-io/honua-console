// Metadata only. Browser File bytes, CSRF tokens and credentials never enter this store.
export class DatasetImportStoreConflict extends Error {
    constructor() { super('Another view updated this import. Check its original status before continuing.'); }
}
export class DatasetImportStore {
    constructor(indexedDB = globalThis.indexedDB) { this.indexedDB = indexedDB; }
    async open() {
        if (!this.indexedDB) throw new Error('Durable import recovery storage is unavailable in this browser.');
        this.database ||= new Promise((resolve, reject) => {
            const request = this.indexedDB.open('honua-dataset-import-v1', 1);
            request.onupgradeneeded = () => request.result.createObjectStore('transfers');
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
            request.onblocked = () => reject(new Error('Import recovery storage is blocked by another Console view.'));
        });
        return this.database;
    }
    async load(key) {
        const database = await this.open();
        return new Promise((resolve, reject) => {
            const transaction = database.transaction('transfers', 'readonly');
            const request = transaction.objectStore('transfers').get(key);
            let value = null;
            request.onsuccess = () => { value = request.result ?? null; };
            transaction.oncomplete = () => resolve(value);
            transaction.onerror = () => reject(transaction.error);
            transaction.onabort = () => reject(transaction.error || new Error('Import recovery read was interrupted.'));
        });
    }
    async save(key, expectedRevision, next) {
        const database = await this.open();
        return new Promise((resolve, reject) => {
            const transaction = database.transaction('transfers', 'readwrite');
            const store = transaction.objectStore('transfers');
            const request = store.get(key);
            let value, failure;
            request.onsuccess = () => {
                if ((request.result?.revision ?? 0) !== expectedRevision) {
                    failure = new DatasetImportStoreConflict(); transaction.abort(); return;
                }
                value = {...next, revision:expectedRevision + 1};
                try { store.put(value, key); }
                catch (error) { failure = error; transaction.abort(); }
            };
            transaction.oncomplete = () => resolve(value);
            transaction.onerror = () => reject(failure || transaction.error);
            transaction.onabort = () => reject(failure || transaction.error || new Error('Import recovery write was interrupted.'));
        });
    }
}
