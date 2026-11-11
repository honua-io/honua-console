// Served Console modules are imported from Blob URLs because static-asset MIME types are not stable.
// A Blob URL has no directory, so a relative import cannot resolve. This loader fetches that one
// dependency and rewrites the import to its own JavaScript Blob before the module loads.
type ServedModule = {
  init: (container: unknown, options: unknown) => Promise<boolean>;
  setBasemap: (container: unknown, name: unknown) => unknown;
  dispose: (container: unknown) => void;
};

type ServedModuleWindow = typeof globalThis & {
  importServedModule?: (modulePath: string) => Promise<ServedModule>;
};

export async function installServedModuleLoader(): Promise<void> {
  const scope = globalThis as ServedModuleWindow;
  if (scope.importServedModule) return;
  scope.importServedModule = async (modulePath: string) => {
    const source = await (await fetch(modulePath)).text();
    const base = new URL(modulePath, location.href);
    const specifier = source.match(/^import '(\.\/[^']+)';/m);
    let text = source;
    let dependencyUrl: string | null = null;
    if (specifier) {
      const dependency = await (await fetch(new URL(specifier[1], base))).text();
      dependencyUrl = URL.createObjectURL(new Blob([dependency], { type: 'text/javascript' }));
      text = source.replace(specifier[0], `import '${dependencyUrl}';`);
    }
    const blobUrl = URL.createObjectURL(new Blob([text], { type: 'text/javascript' }));
    try {
      return await import(/* @vite-ignore */ blobUrl) as ServedModule;
    } finally {
      URL.revokeObjectURL(blobUrl);
      if (dependencyUrl) URL.revokeObjectURL(dependencyUrl);
    }
  };
}

