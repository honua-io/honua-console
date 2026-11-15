(function () {
    function applicationCulture() {
        const language = navigator.languages?.[0] ?? navigator.language;
        try {
            const [culture] = Intl.getCanonicalLocales(language || []);
            if (culture && Intl.DateTimeFormat.supportedLocalesOf([culture]).length > 0) {
                return culture;
            }
        } catch (error) {
            if (!(error instanceof RangeError)) { throw error; }
        }
        console.warn('Unsupported browser locale; starting Honua Console with en-US.');
        return 'en-US';
    }

    // One dropped download of a boot resource used to stop the Console with its error banner:
    // the runtime's own retry (at most two more tries within about 100 ms) did not recover it.
    // Binary boot resources are fetched here with a back-off of about 7.5 s instead. JavaScript
    // modules must stay URLs, and configuration keeps the runtime's own handling.
    const retriedResourceTypes = new Set(['assembly', 'pdb', 'dotnetwasm', 'globalization']);
    const retryDelaysMs = [500, 1000, 2000, 4000];

    function transientStatus(status) {
        return status === 408 || status === 429 || status >= 500;
    }

    function wait(ms) {
        return new Promise(resolve => setTimeout(resolve, ms));
    }

    async function fetchBootResource(defaultUri, integrity) {
        const init = { cache: 'no-cache' };
        if (integrity) { init.integrity = integrity; }
        for (let attempt = 0; ; attempt++) {
            const lastAttempt = attempt === retryDelaysMs.length;
            try {
                const response = await fetch(defaultUri, init);
                if (lastAttempt || !transientStatus(response.status)) { return response; }
            } catch (error) {
                if (lastAttempt) { throw error; }
            }
            await wait(retryDelaysMs[attempt]);
        }
    }

    function loadBootResource(type, name, defaultUri, integrity) {
        return retriedResourceTypes.has(type) ? fetchBootResource(defaultUri, integrity) : undefined;
    }

    Blazor.start({ applicationCulture: applicationCulture(), loadBootResource });
})();
