import { readFile, writeFile } from 'node:fs/promises';
import { join, resolve } from 'node:path';
import type { LoadContext, Plugin } from '@docusaurus/types';
import type { DocMetadataBase, LoadedContent } from '@docusaurus/plugin-content-docs';

// Use Docusaurus metadata so slugs, titles, and published-page filtering match the site.
export default function llmsPlugin({ siteDir, siteConfig }: LoadContext): Plugin {
  let docs: DocMetadataBase[] = [];

  return {
    name: 'kevlar-llms',
    allContentLoaded({ allContent }) {
      const content = allContent['docusaurus-plugin-content-docs']?.default as LoadedContent;
      docs = content.loadedVersions
        .flatMap(version => version.docs)
        .filter(doc => !doc.draft && !doc.unlisted)
        .sort((left, right) => left.permalink.localeCompare(right.permalink));
    },
    async postBuild({ outDir }) {
      if (docs.length === 0) {
        throw new Error('Cannot generate llms.txt without published documentation.');
      }

      const header = `# ${siteConfig.title}\n\n> ${siteConfig.tagline}\n`;
      const pages = await Promise.all(docs.map(async doc => {
        const source = await readFile(resolve(siteDir, doc.source.replace(/^@site\//, '')), 'utf8');
        // Strip YAML only; preserve Markdown and code samples verbatim.
        const markdown = source.replace(/^---\r?\n[\s\S]*?\r?\n---(?:\r?\n|$)/, '').trim();
        const title = doc.title.replace(/[\\[\]]/g, '\\$&');
        const link = `[${title}](${new URL(doc.permalink, siteConfig.url).href})`;
        return { link, markdown };
      }));

      await Promise.all([
        writeFile(join(outDir, 'llms.txt'),
          `${header}\n## Documentation\n\n${pages.map(page => `- ${page.link}`).join('\n')}\n`),
        writeFile(join(outDir, 'llms-full.txt'),
          `${header}\n${pages.map(page => `## ${page.link}\n\n${page.markdown}`).join('\n\n---\n\n')}\n`),
      ]);
    },
  };
}
