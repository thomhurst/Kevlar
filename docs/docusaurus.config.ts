import { themes as prismThemes } from 'prism-react-renderer';
import type { Config } from '@docusaurus/types';
import type * as Preset from '@docusaurus/preset-classic';

const config: Config = {
  title: 'Kevlar',
  tagline: 'Fast resilience for .NET',
  favicon: 'img/logo.svg',

  url: 'https://kevlar.tomlonghurst.dev',
  baseUrl: '/',

  organizationName: 'thomhurst',
  projectName: 'Kevlar',

  onBrokenLinks: 'throw',
  onBrokenAnchors: 'throw',
  markdown: {
    hooks: {
      onBrokenMarkdownLinks: 'throw',
    },
  },

  i18n: {
    defaultLocale: 'en',
    locales: ['en'],
  },

  presets: [
    [
      'classic',
      {
        docs: {
          sidebarPath: './sidebars.ts',
          editUrl: 'https://github.com/thomhurst/Kevlar/tree/main/docs/',
        },
        blog: false,
        theme: {
          customCss: './src/css/custom.css',
        },
      } satisfies Preset.Options,
    ],
  ],

  themeConfig: {
    colorMode: {
      defaultMode: 'dark',
      respectPrefersColorScheme: true,
    },
    navbar: {
      title: 'Kevlar',
      logo: {
        alt: 'Kevlar logo',
        src: 'img/logo.svg',
      },
      items: [
        {
          type: 'docSidebar',
          sidebarId: 'docsSidebar',
          position: 'left',
          label: 'Documentation',
        },
        {
          href: 'pathname:///api/index.html',
          label: 'API',
          position: 'left',
          target: '_self',
        },
        {
          href: 'https://tomlonghurst.dev/',
          label: 'More libraries',
          position: 'right',
        },
        {
          href: 'https://www.nuget.org/packages/Kevlar',
          label: 'NuGet',
          position: 'right',
        },
        {
          href: 'https://github.com/thomhurst/Kevlar',
          label: 'GitHub',
          position: 'right',
        },
        {
          href: 'https://github.com/sponsors/thomhurst',
          label: 'Sponsor',
          position: 'right',
        },
      ],
    },
    footer: {
      style: 'dark',
      links: [
        {
          title: 'Documentation',
          items: [
            { label: 'Getting Started', to: '/docs/getting-started' },
            { label: 'Strategies', to: '/docs/category/strategies' },
            { label: 'API Reference', href: 'pathname:///api/index.html' },
            { label: 'Coming from Polly?', to: '/docs/polly-migration' },
            { label: 'FAQ', to: '/docs/faq' },
          ],
        },
        {
          title: 'More',
          items: [
            { label: 'GitHub', href: 'https://github.com/thomhurst/Kevlar' },
            { label: 'NuGet', href: 'https://www.nuget.org/packages/Kevlar' },
          ],
        },
        {
          title: 'More libraries',
          items: [
            { label: 'TUnit', href: 'https://tunit.dev/' },
            { label: 'ModularPipelines', href: 'https://modularpipelines.tomlonghurst.dev/' },
            { label: 'Respire', href: 'https://respire.tomlonghurst.dev/' },
            { label: 'Dekaf', href: 'https://dekaf.tomlonghurst.dev/' },
            { label: 'BDTest', href: 'https://bdtest.tomlonghurst.dev/' },
            { label: 'All projects', href: 'https://tomlonghurst.dev/' },
          ],
        },
      ],
      copyright: `Copyright © ${new Date().getFullYear()} Tom Longhurst. Built with Docusaurus.`,
    },
    prism: {
      theme: prismThemes.dracula,
      darkTheme: prismThemes.dracula,
      additionalLanguages: ['csharp', 'bash', 'json'],
    },
  } satisfies Preset.ThemeConfig,
};

export default config;
