import { mkdir, readdir, writeFile } from 'node:fs/promises';
import { resolve, join } from 'node:path';
import { fileURLToPath } from 'node:url';

export const galleryDirectory = fileURLToPath(new URL('../artifacts/studio-customer/gallery/', import.meta.url));
const escape = value => value.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;').replaceAll("'", '&#39;');
const slug = value => value.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');

/** Capture only authenticated product milestones, never an IdP or credential surface. */
export async function captureMilestone(page, info, name, directory = galleryDirectory) {
  const url = new URL(page.url());
  if (!/^\/(?:studio(?:\/|$)|operate(?:\/|$)|inbox(?:\/|$))/.test(url.pathname)
      || await page.locator('input[type="password"]').count() !== 0)
    throw new Error('Milestone screenshots require a product page without credential controls');
  await mkdir(directory, { recursive: true });
  const filename = `${slug(info.project.name)}-${slug(info.title).slice(0, 72)}-${info.retry}-${slug(name)}.png`;
  await page.screenshot({ path: join(directory, filename), fullPage: true });
  await info.attach(name, { path: join(directory, filename), contentType: 'image/png' });
}

/** Build one portable gallery, including partial evidence from failed runs. */
export async function buildGallery(directory = galleryDirectory) {
  await mkdir(directory, { recursive: true });
  const files = (await readdir(directory, { withFileTypes: true }))
    .filter(entry => entry.isFile() && entry.name.endsWith('.png')).map(entry => entry.name).sort();
  const cards = files.map(file => `<figure><a href="${encodeURIComponent(file)}"><img loading="lazy" src="${encodeURIComponent(file)}" alt="${escape(file)}"></a><figcaption>${escape(file)}</figcaption></figure>`).join('\n');
  await writeFile(join(directory, 'index.html'), `<!doctype html>
<html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Console journey milestones</title>
<style>body{font:16px system-ui;margin:2rem;background:#f6f8fa;color:#172b4d}main{display:grid;grid-template-columns:repeat(auto-fit,minmax(320px,1fr));gap:1rem}figure{margin:0;background:white;padding:1rem}img{width:100%;height:auto}figcaption{overflow-wrap:anywhere}</style>
<h1>Console journey milestones</h1><p>${files.length} captured milestones. Missing stages are unqualified; consult the journey progress receipts and test results.</p><main>${cards}</main></html>\n`);
  return files;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) await buildGallery();
