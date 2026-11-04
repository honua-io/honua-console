import { strict as assert } from "node:assert";
import { mkdtemp, readdir, readFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { after, test } from "node:test";
import { gzipSync } from "node:zlib";

import { TAR_LIMITS, readTarArchive } from "../lib/tar-archive.mjs";
import { extractTarGz } from "../lib/tar-extraction.mjs";
import { readTarGz } from "../vendor-assets.mjs";
import {
  GNU_FORMAT,
  HEADER_OFFSETS,
  tarEntry,
  tarGz,
  withHeaderField,
  withUnsealedHeaderField,
  withUtf8HeaderField,
} from "./helpers/tar-fixtures.mjs";

const scratch = await mkdtemp(join(tmpdir(), "honua-console-tar-archive-"));
after(() => rm(scratch, { recursive: true, force: true }));

const file = (name, text = "x") => tarEntry(name, { body: Buffer.from(text) });
const directory = (name) => tarEntry(name, { type: "5" });

async function assertRefused(archive, reason, limits = TAR_LIMITS) {
  const destination = await mkdtemp(join(scratch, "refused-"));
  await assert.rejects(extractTarGz(archive, destination, limits), reason);
  assert.deepEqual(await readdir(destination), [], "nothing is written from a refused archive");
}

test("regular files and directories extract, including a directory listed after its children", async () => {
  const destination = await mkdtemp(join(scratch, "accepted-"));
  await extractTarGz(tarGz(directory("./"), file("./assets/worker.js", "worker"), directory("./assets/"), file("./a.js")), destination);
  assert.equal(await readFile(join(destination, "assets/worker.js"), "utf8"), "worker");
  assert.deepEqual((await readdir(destination)).sort(), ["a.js", "assets"]);
});

test("a header whose checksum does not match is refused", async () => {
  await assertRefused(tarGz(withUnsealedHeaderField(file("./a.js"), 0, "b")), /checksum does not match/);
});

test("numeric header fields must be complete non-negative octal", async () => {
  for (const size of ["0garbage\0", "-0000000001\0", "\u0080\0\0\0\0\0\0\0\0\0\0\0", "           \0"]) {
    await assertRefused(tarGz(withHeaderField(file("./a.js"), HEADER_OFFSETS.size, size)), /malformed size field/);
  }
  await assertRefused(tarGz(withUnsealedHeaderField(file("./a.js"), HEADER_OFFSETS.checksum, "12x456\0 ")), /malformed checksum field/);
});

test("only ustar and GNU tar headers are read", async () => {
  for (const format of ["\0\0\0\0\0\0\0\0", "ustar\u000001", "pax  \u0000\u0000\u0000"]) {
    await assertRefused(tarGz(withHeaderField(file("./a.js"), HEADER_OFFSETS.format, format)), /not ustar or GNU tar/);
  }
});

const gnuFile = (name, timeBytes) =>
  withHeaderField(withHeaderField(file(name), HEADER_OFFSETS.format, GNU_FORMAT), HEADER_OFFSETS.prefix, timeBytes);

test("a GNU header's access and change times are not read as a POSIX name prefix", async () => {
  const destination = await mkdtemp(join(scratch, "gnu-"));
  await extractTarGz(tarGz(gnuFile("./a.js", "15272616132\u000015272616133")), destination);
  assert.deepEqual(await readdir(destination), ["a.js"]);
});

test("GNU entries with the same name but different time fields are duplicates", async () => {
  await assertRefused(
    tarGz(gnuFile("./a.js", "15272616132\u0000"), gnuFile("./a.js", "15272616199\u0000")),
    /duplicate|more than once/i,
  );
});

test("entry types other than regular files and directories are refused", async () => {
  for (const type of ["1", "2", "3", "4", "6", "7", "x", "g", "L", "K", "\0"]) {
    await assertRefused(tarGz(withHeaderField(file("./a.js"), HEADER_OFFSETS.type, type)), /unsupported type/);
  }
  await assertRefused(tarGz(tarEntry("./assets/", { type: "5", body: Buffer.from("x") })), /carries 1 bytes/);
});

test("the archive must end with a complete end-of-archive marker and nothing after it", async () => {
  await assertRefused(gzipSync(file("./a.js")), /without a tar end-of-archive marker/);
  await assertRefused(gzipSync(Buffer.concat([file("./a.js"), Buffer.alloc(512)])), /instead of a tar end-of-archive marker/);
  await assertRefused(
    gzipSync(Buffer.concat([file("./a.js"), Buffer.alloc(512), file("./hidden.js"), Buffer.alloc(1024)])),
    /instead of a tar end-of-archive marker/,
  );
  await assertRefused(gzipSync(file("./a.js", "x".repeat(600)).subarray(0, 1024)), /truncated/);
});

test("paths outside the extraction directory are refused", async () => {
  for (const name of ["../escape.js", "/tmp/escape.js", "./assets/../../escape.js", "assets\\escape.js"]) {
    await assertRefused(tarGz(file(name)), /escapes the extraction directory/);
  }
  await assertRefused(tarGz(file("./")), /names no file/);
});

test("duplicate and conflicting entries are refused, including normalized aliases", async () => {
  const conflicts = [
    [[directory("assets/"), directory("./assets/")], /duplicates an earlier entry/],
    [[file("a.js"), file("./a.js")], /duplicates an earlier entry/],
    [[file("./assets"), directory("./assets/")], /duplicates an earlier entry/],
    [[file("./assets"), file("./assets/worker.js")], /inside the file assets/],
    [[file("./assets/worker.js"), file("./assets")], /replaces a directory of earlier entries/],
  ];
  for (const [entries, reason] of conflicts) await assertRefused(tarGz(...entries), reason);
});

test("entry size, entry count and inflated size are bounded", async () => {
  const limits = { maxInflatedBytes: 8192, maxEntries: 2, maxEntryBytes: 600 };
  await assertRefused(tarGz(file("./big.js", "x".repeat(601))), /big\.js exceeds 600 bytes/, limits);
  await assertRefused(tarGz(file("./a.js"), file("./b.js"), file("./c.js")), /more than 2 entries/, limits);
  const bulky = [1, 2].map((index) => file(`./part-${index}.js`, "x".repeat(4000)));
  await assertRefused(tarGz(...bulky), /inflates beyond 8192 bytes/, { ...limits, maxEntryBytes: 4096 });
});

test("the default limits leave headroom above the v0.1.1 release", () => {
  const release = { inflatedBytes: 8_376_320, entries: 33, largestEntryBytes: 1_948_675 };
  assert.ok(TAR_LIMITS.maxInflatedBytes >= 10 * release.inflatedBytes);
  assert.ok(TAR_LIMITS.maxEntries >= 100 * release.entries);
  assert.ok(TAR_LIMITS.maxEntryBytes >= 10 * release.largestEntryBytes);
});

test("owner fields may be empty only for a caller that allows it", () => {
  const ownerless = withHeaderField(withHeaderField(file("package/LICENSE", "MIT"), 108, "\0".repeat(8)), 116, "\0".repeat(8));
  assert.throws(() => readTarArchive(tarGz(ownerless)), /malformed uid field/);
  const [entry] = readTarArchive(tarGz(ownerless), { emptyNumericFields: ["uid", "gid"] });
  assert.equal(entry.body.toString(), "MIT");
  assert.throws(
    () => readTarArchive(tarGz(withHeaderField(ownerless, 108, "12x\0\0\0\0\0")), { emptyNumericFields: ["uid", "gid"] }),
    /malformed uid field/,
  );
});

test("entry names are UTF-8 in the short name, the POSIX prefix and a GNU long name", async () => {
  const destination = await mkdtemp(join(scratch, "utf8-"));
  await extractTarGz(tarGz(file("café.js", "short")), destination);
  assert.equal(await readFile(join(destination, "café.js"), "utf8"), "short");

  const prefixed = withUtf8HeaderField(file("café.js", "pref"), {
    offset: HEADER_OFFSETS.prefix,
    value: "目录",
    length: 155,
  });
  const [entry] = readTarArchive(tarGz(prefixed));
  assert.equal(entry.name, "目录/café.js");

  const longName = `package/${"nést/".repeat(20)}café.js`;
  assert.ok(Buffer.byteLength(longName) > 100);
  const files = readTarGz(
    tarGz(tarEntry("././@LongLink", { type: "L", body: Buffer.from(`${longName}\0`) }), file("x", "long")),
  );
  assert.deepEqual([...files.keys()], [longName]);
});

test("npm vendoring reads regular files through the shared parser and skips other entry types", () => {
  const longName = `package/${"nested/".repeat(16)}module.js`;
  const archive = tarGz(
    tarEntry("package/pax", { type: "x", body: Buffer.from("27 path=package/ignored.js\n") }),
    file("package/LICENSE", "MIT"),
    directory("package/dist/"),
    tarEntry("././@LongLink", { type: "L", body: Buffer.from(`${longName}\0`) }),
    file(longName.slice(0, 99), "long"),
    tarEntry("package/link.js", { type: "2", link: "LICENSE" }),
  );
  const files = readTarGz(archive);
  assert.deepEqual([...files.keys()], ["package/LICENSE", longName]);
  assert.equal(files.get(longName).toString(), "long");
  assert.throws(() => readTarGz(tarGz(withUnsealedHeaderField(file("package/LICENSE"), 0, "q"))), /checksum does not match/);
});
