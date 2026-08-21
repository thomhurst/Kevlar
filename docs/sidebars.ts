import type { SidebarsConfig } from '@docusaurus/plugin-content-docs';

const sidebars: SidebarsConfig = {
  docsSidebar: [
    'intro',
    'getting-started',
    'handling-failures',
    {
      type: 'category',
      label: 'Strategies',
      link: {
        type: 'generated-index',
        title: 'Strategies',
        description:
          'Every resilience behaviour in Kevlar is a strategy. Chain them fluently — the first one you add is the outermost.',
        slug: '/category/strategies',
      },
      items: [
        'strategies/retry',
        'strategies/circuit-breaker',
        'strategies/timeout',
        'strategies/rate-limit',
        'strategies/concurrency-limit',
        'strategies/hedging',
        'strategies/fallback',
      ],
    },
    'composition',
    'executing',
    'dependency-injection',
    'http',
    'custom-strategies',
    'library-authors',
    'analyzers',
    'observability',
    'testing',
    'performance',
    'benchmarks',
    'stress-tests',
    'polly-migration',
  ],
};

export default sidebars;
