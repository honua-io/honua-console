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

    Blazor.start({ applicationCulture: applicationCulture() });
})();
