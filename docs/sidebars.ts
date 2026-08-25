import type { SidebarsConfig } from '@docusaurus/plugin-content-docs';

const sidebars: SidebarsConfig = {
  docsSidebar: [
    'intro',
    'getting-started',
    'polly-migration',
    'handling-failures',
    'exceptions',
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
    'partitioning',
    'executing',
    'dependency-injection',
    'http',
    'chaos',
    'grpc',
    'custom-strategies',
    'library-authors',
    'analyzers',
    'logging',
    'observability',
    'testing',
    'performance',
    'benchmarks',
    'stress-tests',
    'support-policy',
    'thread-safety',
    'faq',
  ],
};

export default sidebars;
