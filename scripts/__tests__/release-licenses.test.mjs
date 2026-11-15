import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { readFileSync, readdirSync } from "node:fs";
import { dirname, join, relative, resolve } from "node:path";
import { test } from "node:test";
import { fileURLToPath } from "node:url";

// The static release ships the .NET runtime, ASP.NET Core WebAssembly and Polly in _framework/.
// Their licences require the notices to travel with every copy, so the texts under
// release/licenses/ must match the versions the browser app actually locks.
const root = resolve(dirname(fileURLToPath(import.meta.url)), "../..");
const licenses = join(root, "release/licenses");
const sources = JSON.parse(readFileSync(join(licenses, "sources.json"), "utf8")).files;
const locked = JSON.parse(readFileSync(join(root, "src/Honua.Console.Web/packages.lock.json"), "utf8")).dependencies["net11.0"];

function filesUnder(directory) {
  return readdirSync(directory, { withFileTypes: true }).flatMap(entry =>
    entry.isDirectory() ? filesUnder(join(directory, entry.name)) : [join(directory, entry.name)]);
}

test("release licence texts are the recorded upstream bytes for the locked versions", () => {
  for (const entry of sources) {
    const bytes = readFileSync(join(licenses, entry.file));
    assert.equal(createHash("sha256").update(bytes).digest("hex"), entry.sha256, entry.file);
    assert.equal(locked[entry.lockedPackage]?.resolved, entry.version,
      `${entry.file} is for ${entry.lockedPackage} ${entry.version}; refresh it when the lock moves`);
  }
  const listed = new Set([...sources.map(entry => entry.file), "sources.json"]);
  assert.deepEqual(filesUnder(licenses).map(path => relative(licenses, path).split("\\").join("/")).sort(), [...listed].sort());
});

test("every third-party package in the browser app has its licence text", () => {
  // Microsoft packages carry the .NET Foundation MIT licence in dotnet-runtime/LICENSE.TXT; the Honua
  // SDK packages are Apache-2.0 from the same licensor as the Console and covered by its LICENSE.
  const thirdParty = Object.entries(locked).filter(([name, info]) =>
    info.type !== "Project" && !/^(Microsoft|System|runtime|NETStandard|Honua)\./u.test(name));
  assert.ok(thirdParty.length > 0, "the lock lists the packages the bundle ships");
  for (const [name, info] of thirdParty) {
    const family = name.split(".")[0];
    const entry = sources.find(source => source.lockedPackage.split(".")[0] === family);
    assert.ok(entry, `${name} ships in _framework/ without a licence under release/licenses/`);
    assert.equal(info.resolved, entry.version, `${name} ${info.resolved} differs from its licence text (${entry.version})`);
  }
});
