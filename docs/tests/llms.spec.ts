import { expect, test } from '@playwright/test';
import { readFile, readdir } from 'node:fs/promises';
import { join } from 'node:path';

test('LLM documentation endpoints serve the index and full Markdown', async ({ request }) => {
  const indexResponse = await request.get('/llms.txt');
  const fullResponse = await request.get('/llms-full.txt');
  expect(indexResponse.status()).toBe(200);
  expect(fullResponse.status()).toBe(200);
  expect(indexResponse.headers()['content-type']).toContain('text/plain');
  expect(fullResponse.headers()['content-type']).toContain('text/plain');

  const index = await indexResponse.text();
  const full = await fullResponse.text();
  expect(index).toMatch(/^# Kevlar\s/);
  expect(full).toMatch(/^# Kevlar\s/);

  // Check every source page, including nested strategies and both migration guides.
  const sources = (await readdir('docs', { recursive: true }))
    .filter(file => /\.mdx?$/.test(file));
  expect(sources.length).toBeGreaterThan(0);
  const links = [...index.matchAll(/^- \[.+\]\((https:\/\/[^)]+)\)$/gm)]
    .map(match => new URL(match[1]));
  expect(links).toHaveLength(sources.length);

  for (const file of sources) {
    const path = `/docs/${file.replace(/\\/g, '/').replace(/\.mdx?$/, '')}`;
    const link = links.find(url => url.pathname === path);
    expect(link, file).toBeDefined();
    expect(link!.origin).toBe('https://kevlar.tomlonghurst.dev');
    expect((await request.get(link!.pathname)).status(), file).toBe(200);

    const source = await readFile(join('docs', file), 'utf8');
    const body = source.slice(source.indexOf('\n# ')).trim();
    expect(body, file).not.toBe('');
    expect(full, file).toContain(body);
    expect(full, file).toContain(link!.href);
  }
  expect(full).not.toContain('sidebar_position:');
});
