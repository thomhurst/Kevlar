import type { ReactNode } from 'react';
import clsx from 'clsx';
import Link from '@docusaurus/Link';
import useDocusaurusContext from '@docusaurus/useDocusaurusContext';
import Layout from '@theme/Layout';
import CodeBlock from '@theme/CodeBlock';
import Heading from '@theme/Heading';

import styles from './index.module.css';

const heroSample = `var policy = Policy
    .Timeout(TimeSpan.FromSeconds(30))   // total budget
    .Retry(3)                            // backoff + jitter built in
    .CircuitBreaker(5, breakDuration: TimeSpan.FromSeconds(30));

var user = await policy.ExecuteAsync(
    ct => LoadUserAsync(id, ct), cancellationToken);`;

const pollySample = `var pipeline = new ResiliencePipelineBuilder()
    .AddTimeout(TimeSpan.FromSeconds(30))
    .AddRetry(new RetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
    })
    .AddCircuitBreaker(new CircuitBreakerStrategyOptions
    {
        FailureRatio = 1.0,
        MinimumThroughput = 5,
        BreakDuration = TimeSpan.FromSeconds(30),
    })
    .Build();`;

const kevlarSample = `var policy = Policy
    .Timeout(TimeSpan.FromSeconds(30))
    .Retry(3)
    .CircuitBreaker(5, breakDuration: TimeSpan.FromSeconds(30));`;

type FeatureItem = {
  title: string;
  description: ReactNode;
};

const features: FeatureItem[] = [
  {
    title: 'Intuitive first',
    description: (
      <>
        <code>Policy.Handle&lt;TimeoutException&gt;().Retry(3)</code> reads like
        what it does. No context pooling ceremony, no predicate-builder classes —
        and full options objects when you want them.
      </>
    ),
  },
  {
    title: 'Fast',
    description: (
      <>
        Outcomes flow between pipeline layers as structs instead of thrown
        exceptions; contexts are pooled internally; state-passing overloads
        eliminate closures; <code>ValueTask</code> end to end.
      </>
    ),
  },
  {
    title: 'Production defaults',
    description: (
      <>
        <code>Policy.Retry(3)</code> gives you exponential backoff{' '}
        <em>with jitter</em> capped at 30s — the thing you'd have configured
        anyway.
      </>
    ),
  },
  {
    title: 'Composable',
    description: (
      <>
        Policies merge with <code>Wrap</code> and <code>Compose</code>, chain
        fluently, and stateful strategies intentionally share their state
        wherever the same policy instance is reused.
      </>
    ),
  },
  {
    title: 'Zero dependencies',
    description: (
      <>
        The core package depends on nothing but the BCL. No transitive baggage
        in your dependency tree.
      </>
    ),
  },
  {
    title: 'Broad reach',
    description: (
      <>
        <code>netstandard2.0</code> (covers .NET Framework 4.6.2+) and{' '}
        <code>net8.0</code> targets, with satellites for Microsoft DI and{' '}
        <code>HttpClientFactory</code>.
      </>
    ),
  },
];

function Feature({ title, description }: FeatureItem) {
  return (
    <div className={clsx('col col--4')}>
      <div className={styles.featureCard}>
        <Heading as="h3">{title}</Heading>
        <p>{description}</p>
      </div>
    </div>
  );
}

function HomepageHeader() {
  const { siteConfig } = useDocusaurusContext();
  return (
    <header className={styles.hero}>
      <div className="container">
        <div className={styles.heroInner}>
          <div className={styles.heroText}>
            <img
              src="img/logo.svg"
              alt=""
              className={styles.heroLogo}
              width={96}
              height={96}
            />
            <Heading as="h1" className={styles.heroTitle}>
              {siteConfig.title}
            </Heading>
            <p className={styles.heroTagline}>
              Retries, circuit breakers, timeouts, rate limiting, bulkheads,
              hedging and fallbacks — composed through one fluent,
              allocation-conscious policy API.
            </p>
            <div className={styles.buttons}>
              <Link
                className="button button--primary button--lg"
                to="/docs/getting-started">
                Get Started
              </Link>
              <Link
                className="button button--secondary button--outline button--lg"
                href="https://github.com/thomhurst/Kevlar">
                GitHub
              </Link>
            </div>
          </div>
          <div className={styles.heroCode}>
            <CodeBlock language="csharp">{heroSample}</CodeBlock>
          </div>
        </div>
      </div>
    </header>
  );
}

function Comparison() {
  return (
    <section className={styles.comparison}>
      <div className="container">
        <Heading as="h2" className="text--center">
          Same pipeline, less ceremony
        </Heading>
        <p className="text--center">
          Everything Polly v8 can express, without the options-object tax.
        </p>
        <div className="row">
          <div className="col col--6">
            <h3 className="text--center">Polly v8</h3>
            <CodeBlock language="csharp">{pollySample}</CodeBlock>
          </div>
          <div className="col col--6">
            <h3 className="text--center">Kevlar</h3>
            <CodeBlock language="csharp">{kevlarSample}</CodeBlock>
            <div className={styles.benchCallout}>
              <p>
                And it's quicker, too — <strong>100 ns / 0 B</strong> for a
                successful <code>Retry(3)</code> call vs 154 ns / 24 B for Polly
                v8. <Link to="/docs/performance">See the benchmarks →</Link>
              </p>
            </div>
          </div>
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
      description="Fast, zero-dependency resilience for .NET — retries, circuit breakers, timeouts, rate limiting, bulkheads, hedging and fallbacks through one fluent policy API.">
      <HomepageHeader />
      <main>
        <section className={styles.features}>
          <div className="container">
            <div className="row">
              {features.map((props, idx) => (
                <Feature key={idx} {...props} />
              ))}
            </div>
          </div>
        </section>
        <Comparison />
      </main>
    </Layout>
  );
}
