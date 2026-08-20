import type { ReactNode } from 'react';
import Link from '@docusaurus/Link';
import useDocusaurusContext from '@docusaurus/useDocusaurusContext';
import Layout from '@theme/Layout';
import CodeBlock from '@theme/CodeBlock';
import Heading from '@theme/Heading';

import styles from './index.module.css';

const heroSample = `var shield = Shield
    .Timeout(TimeSpan.FromSeconds(30))
    .Retry(3)
    .CircuitBreaker(
        5,
        breakDuration: TimeSpan.FromSeconds(30));

var user = await shield.ExecuteAsync(
    ct => LoadUserAsync(id, ct), cancellationToken);`;

const kevlarSample = `var shield = Shield
    .Timeout(TimeSpan.FromSeconds(30))
    .Retry(3)
    .CircuitBreaker(5,
        breakDuration: TimeSpan.FromSeconds(30));`;

const advancedSample = `var search = Shield.For<HttpResponseMessage>()
    .When<HttpRequestException>()
    .OrResult(r => (int)r.StatusCode is >= 500 or 429)
    .Fallback((outcome, ct) => cache.GetCachedAsync(ct))
    .Retry(o =>
    {
        o.MaxRetries = 4;
        o.DelayGenerator = e =>
            e.Outcome.Result?.Headers.RetryAfter?.Delta;
        o.OnRetry = e => logger.LogWarning(
            "retry {Attempt} in {Delay}", e.Attempt, e.Delay);
    })
    .CircuitBreaker(o =>
    {
        o.FailureRatio = 0.5;
        o.SamplingWindow = TimeSpan.FromSeconds(30);
        o.Monitor = monitor;
    })
    .Hedge(maxAttempts: 2, delay: TimeSpan.FromMilliseconds(150))
    .Timeout(TimeSpan.FromSeconds(2))
    .WithName("search");`;

const strategies = [
  'Retry',
  'Circuit breaker',
  'Timeout',
  'Rate limit',
  'Concurrency limit',
  'Hedging',
  'Fallback',
];

type FeatureItem = {
  number: string;
  title: string;
  description: ReactNode;
  accent: string;
};

const features: FeatureItem[] = [
  {
    number: '01',
    title: 'Readable by design',
    accent: 'API',
    description: (
      <>
        <code>Shield.When&lt;TimeoutException&gt;().Retry(3)</code> reads like
        intent, not infrastructure. Use concise defaults or take full control
        with options objects.
      </>
    ),
  },
  {
    number: '02',
    title: 'Built for the hot path',
    accent: '100 ns',
    description: (
      <>
        Struct outcomes, pooled contexts, state-passing overloads and{' '}
        <code>ValueTask</code> end to end keep successful calls fast and
        allocation-free.
      </>
    ),
  },
  {
    number: '03',
    title: 'Defaults you can ship',
    accent: 'SAFE',
    description: (
      <>
        <code>Shield.Retry(3)</code> includes exponential backoff with jitter,
        capped at 30 seconds—the production setting you wanted anyway.
      </>
    ),
  },
  {
    number: '04',
    title: 'Composition, not ceremony',
    accent: 'FLUENT',
    description: (
      <>
        Chain strategies fluently. Merge shields with <code>Wrap</code> and{' '}
        <code>Compose</code>. Reuse one instance when state should be shared.
      </>
    ),
  },
  {
    number: '05',
    title: 'Nothing hiding underneath',
    accent: '0 DEPS',
    description: (
      <>
        Core Kevlar depends on nothing but the BCL. No surprise transitive
        packages, version conflicts or dependency-tree baggage.
      </>
    ),
  },
  {
    number: '06',
    title: 'Old apps. New apps.',
    accent: '.NET',
    description: (
      <>
        Targets <code>netstandard2.0</code> and <code>net8.0</code>, with
        integrations for Microsoft DI and <code>HttpClientFactory</code>.
      </>
    ),
  },
];

function ArrowIcon() {
  return (
    <svg viewBox="0 0 20 20" aria-hidden="true">
      <path d="M4 10h11M11 6l4 4-4 4" />
    </svg>
  );
}

function Feature({ number, title, description, accent }: FeatureItem) {
  return (
    <article className={styles.featureCard}>
      <div className={styles.featureMeta}>
        <span>{number}</span>
        <span>{accent}</span>
      </div>
      <Heading as="h3">{title}</Heading>
      <p>{description}</p>
      <div className={styles.featureWeave} aria-hidden="true" />
    </article>
  );
}

function HomepageHeader() {
  return (
    <header className={styles.hero}>
      <div className={styles.heroGrid} aria-hidden="true" />
      <div className="container">
        <div className={styles.heroInner}>
          <div className={styles.heroText}>
            <div className={styles.eyebrow}>
              <span className={styles.statusDot} />
              Resilience engineering, refined
            </div>
            <Heading as="h1" className={styles.heroTitle}>
              Code that stays
              <span> standing.</span>
            </Heading>
            <p className={styles.heroTagline}>
              Fast, zero-dependency resilience for .NET. Every strategy you
              need, composed through one fluent shield API.
            </p>
            <div className={styles.buttons}>
              <Link className={styles.primaryButton} to="/docs/getting-started">
                Build your first shield
                <ArrowIcon />
              </Link>
              <Link
                className={styles.secondaryButton}
                href="https://github.com/thomhurst/Kevlar">
                View on GitHub
              </Link>
            </div>
            <div className={styles.installLine}>
              <span>INSTALL</span>
              <code>dotnet add package Kevlar</code>
            </div>
          </div>

          <div className={styles.heroVisual}>
            <div className={styles.codeGlow} aria-hidden="true" />
            <div className={styles.codeWindow}>
              <div className={styles.codeBar}>
                <div className={styles.windowDots} aria-hidden="true">
                  <span />
                  <span />
                  <span />
                </div>
                <span>ResilientUserService.cs</span>
                <span className={styles.liveLabel}>SHIELD ACTIVE</span>
              </div>
              <CodeBlock language="csharp">{heroSample}</CodeBlock>
              <div className={styles.codeFooter}>
                <span><i /> Request protected</span>
                <span>0 B allocated</span>
              </div>
            </div>
            <div className={styles.floatingBadge} aria-hidden="true">
              <span>7</span>
              strategies
            </div>
          </div>
        </div>
      </div>
    </header>
  );
}

function StrategyRail() {
  return (
    <section className={styles.strategyRail} aria-label="Available strategies">
      <div className="container">
        <div className={styles.strategyInner}>
          <span className={styles.strategyLabel}>One shield. Every failure mode.</span>
          <div className={styles.strategyList}>
            {strategies.map((strategy) => (
              <span key={strategy}>{strategy}</span>
            ))}
          </div>
        </div>
      </div>
    </section>
  );
}

function Features() {
  return (
    <section className={styles.features}>
      <div className="container">
        <div className={styles.sectionIntro}>
          <div>
            <span className={styles.kicker}>Engineered differently</span>
            <Heading as="h2">Less framework.<br />More resilience.</Heading>
          </div>
          <p>
            Kevlar strips away accidental complexity without giving up the
            control, performance or observability production systems need.
          </p>
        </div>
        <div className={styles.featureGrid}>
          {features.map((feature) => (
            <Feature key={feature.number} {...feature} />
          ))}
        </div>
      </div>
    </section>
  );
}

function ProofBand() {
  return (
    <section className={styles.proofBand} aria-label="Kevlar performance summary">
      <div className="container">
        <div className={styles.proofGrid}>
          <div className={styles.proofLead}>
            <span className={styles.kicker}>Measured, not marketed</span>
            <Heading as="h2">Tiny overhead.<br />Serious protection.</Heading>
          </div>
          <div className={styles.metric}>
            <strong>100<span> ns</span></strong>
            <p>Successful Retry(3) execution</p>
          </div>
          <div className={styles.metric}>
            <strong>0<span> B</span></strong>
            <p>Allocated on the happy path</p>
          </div>
          <div className={styles.metric}>
            <strong>0<span> deps</span></strong>
            <p>In the core package</p>
          </div>
        </div>
        <Link className={styles.proofLink} to="/docs/performance">
          Explore the benchmarks <ArrowIcon />
        </Link>
      </div>
    </section>
  );
}

function PipelineShowcase() {
  return (
    <section className={styles.pipelineShowcase}>
      <div className="container">
        <div className={styles.pipelineHeader}>
          <div>
            <span className={styles.kicker}>Designed for intent</span>
            <Heading as="h2">Protection that<br />reads like code.</Heading>
          </div>
          <p>
            Build complete resilience pipelines from focused strategies.
            Sensible defaults keep common cases concise; options objects give
            you full control when you need it.
          </p>
        </div>
        <div className={styles.pipelineGrid}>
          <article className={styles.pipelineCard}>
            <div className={styles.pipelineTitle}>
              <span>Kevlar</span>
              <span>Sensible defaults</span>
            </div>
            <CodeBlock language="csharp">{kevlarSample}</CodeBlock>
            <div className={styles.pipelineResult}>
              <span>Timeout · Retry · Circuit breaker</span>
              <strong>One fluent API</strong>
            </div>
          </article>
          <article className={styles.pipelineCard}>
            <div className={styles.pipelineTitle}>
              <span>Kevlar</span>
              <span>Full control</span>
            </div>
            <CodeBlock language="csharp">{advancedSample}</CodeBlock>
            <div className={styles.pipelineResult}>
              <span>Typed results · Retry-After · Hedging · Fallback</span>
              <strong>Same fluent API</strong>
            </div>
          </article>
        </div>
      </div>
    </section>
  );
}

function FinalCta() {
  return (
    <section className={styles.finalCta}>
      <div className="container">
        <div className={styles.ctaPanel}>
          <div className={styles.ctaMark} aria-hidden="true">K</div>
          <div>
            <span className={styles.kicker}>Ready for impact</span>
            <Heading as="h2">Wrap your first call in minutes.</Heading>
          </div>
          <Link className={styles.primaryButton} to="/docs/getting-started">
            Get started
            <ArrowIcon />
          </Link>
        </div>
      </div>
    </section>
  );
}

export default function Home(): ReactNode {
  const { siteConfig } = useDocusaurusContext();
  return (
    <Layout
      title={siteConfig.title}
      description="Fast, zero-dependency resilience for .NET — retries, circuit breakers, timeouts, rate limiting, concurrency limits, hedging and fallbacks through one fluent shield API.">
      <HomepageHeader />
      <main className={styles.main}>
        <StrategyRail />
        <Features />
        <ProofBand />
        <PipelineShowcase />
        <FinalCta />
      </main>
    </Layout>
  );
}
