import type { ReactNode } from 'react';
import Link from '@docusaurus/Link';
import useBaseUrl from '@docusaurus/useBaseUrl';
import Layout from '@theme/Layout';
import CodeBlock from '@theme/CodeBlock';
import Heading from '@theme/Heading';

import styles from './index.module.css';

const shieldSample = `using Kevlar;

var shield = Shield
    .Timeout(TimeSpan.FromSeconds(30))
    .Retry(3)
    .CircuitBreaker(
        consecutiveFailures: 5,
        breakDuration: TimeSpan.FromSeconds(30));

var user = await shield.ExecuteAsync(
    ct => LoadUserAsync(id, ct), cancellationToken);`;

const strategies = [
  { title: 'Retry', slug: 'retry', symbol: '↻', description: 'Give transient failures another chance.' },
  { title: 'Circuit breaker', slug: 'circuit-breaker', symbol: '⌁', description: 'Let struggling dependencies recover.' },
  { title: 'Timeout', slug: 'timeout', symbol: '◷', description: 'Keep every call within its budget.' },
  { title: 'Rate limit', slug: 'rate-limit', symbol: '≋', description: 'Set a sustainable pace for requests.' },
  { title: 'Concurrency limit', slug: 'concurrency-limit', symbol: '⇉', description: 'Keep concurrent work under control.' },
  { title: 'Hedging', slug: 'hedging', symbol: '⑂', description: 'Race another attempt against a slow call.' },
  { title: 'Fallback', slug: 'fallback', symbol: '↳', description: 'Have an answer when a dependency fails.' },
];

const features = [
  {
    label: '01 / PERFORMANCE',
    title: 'Light on the hot path.',
    description: <>Struct outcomes, pooled contexts and <code>ValueTask</code> keep successful calls fast. State-passing overloads avoid closure allocations.</>,
    link: '/docs/performance',
    linkLabel: 'Explore performance',
  },
  {
    label: '02 / CONTROL',
    title: 'Your intent. Your rules.',
    description: <><code>Shield.When&lt;TimeoutExceededException&gt;().Retry(3)</code> says what you mean. Start with sensible defaults; fine-tune with options.</>,
    link: '/docs/strategies/retry',
    linkLabel: 'Meet the fluent API',
  },
  {
    label: '03 / COMPATIBILITY',
    title: 'Ready for your stack.',
    description: <>Targets <code>netstandard2.0</code> and <code>net10.0</code>. NativeAOT-ready, with integrations for Microsoft DI and <code>HttpClientFactory</code>.</>,
    link: '/docs/getting-started',
    linkLabel: 'Find your starting point',
  },
];

function ArrowIcon() {
  return (
    <svg viewBox="0 0 20 20" fill="none" aria-hidden="true">
      <path d="M4 10h11M11 6l4 4-4 4" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}

function ShieldIllustration() {
  return (
    <div className={styles.shieldVisual} aria-hidden="true">
      <div className={styles.orbit} />
      <div className={styles.orbitInner} />
      <span className={styles.visualLabel}>ENGINEERED FOR THE UNEXPECTED</span>
      <svg className={styles.shieldArt} viewBox="0 0 480 480" fill="none">
        <defs>
          <linearGradient id="shield-edge" x1="130" y1="80" x2="360" y2="390" gradientUnits="userSpaceOnUse">
            <stop stopColor="#ffe4a0" /><stop offset=".45" stopColor="#f0ae28" /><stop offset="1" stopColor="#80500b" />
          </linearGradient>
          <linearGradient id="shield-face" x1="150" y1="100" x2="350" y2="370" gradientUnits="userSpaceOnUse">
            <stop stopColor="#82602a" /><stop offset=".5" stopColor="#3b301b" /><stop offset="1" stopColor="#171a16" />
          </linearGradient>
          <pattern id="shield-weave" width="16" height="16" patternUnits="userSpaceOnUse" patternTransform="rotate(-30)">
            <path d="M0 4h16M0 12h16" stroke="#e5b751" strokeWidth="5" opacity=".24" />
            <path d="M4 0v16M12 0v16" stroke="#0c100d" strokeWidth="5" opacity=".7" />
            <path d="M4 0v8M12 8v8" stroke="#ebc46e" strokeWidth="5" opacity=".28" />
          </pattern>
        </defs>
        <g stroke="#f0ae28" strokeWidth="1" opacity=".22">
          <path d="M240 22v38M240 424v34M22 240h38M420 240h38" />
          <path d="M80 108l42 33M358 141l42-33M82 374l45-35M352 341l46 33" strokeDasharray="3 5" />
          <path d="M240 51 389 102v132c0 90-66 150-149 196-83-46-149-106-149-196V102Z" />
        </g>
        <path d="m240 85 125 43v111c0 75-55 126-125 165-70-39-125-90-125-165V128Z" fill="#080b09" stroke="#674c20" strokeWidth="2" transform="translate(0 12)" />
        <path d="m240 75 125 43v111c0 75-55 126-125 165-70-39-125-90-125-165V118Z" fill="url(#shield-face)" stroke="url(#shield-edge)" strokeWidth="3" />
        <path d="m240 87 114 39v103c0 68-49 116-114 153-65-37-114-85-114-153V126Z" fill="url(#shield-weave)" stroke="#b18a3a" strokeOpacity=".45" />
        <path d="m240 111 93 32v85c0 58-40 99-93 132-53-33-93-74-93-132v-85Z" stroke="#f8d47c" strokeOpacity=".3" />
        <path d="M216 181v104m4-47 48-57m-46 55 49 49" stroke="#0b0e0b" strokeWidth="18" strokeLinejoin="bevel" />
        <path d="M216 177v104m4-47 48-57m-46 55 49 49" stroke="url(#shield-edge)" strokeWidth="12" strokeLinejoin="bevel" />
        <path d="m139 132 101-35 101 35" stroke="#ffe4a0" strokeOpacity=".65" />
      </svg>
      <span className={`${styles.faultTag} ${styles.faultTimeout}`}>TIMEOUT <span>↗</span></span>
      <span className={`${styles.faultTag} ${styles.faultRetry}`}>503 <span>↗</span></span>
      <span className={`${styles.faultTag} ${styles.faultLatency}`}>LATENCY <span>↗</span></span>
      <div className={styles.shieldStatus}><span /> YOUR CALL. PROTECTED.</div>
      <span className={styles.visualIndex}>FIG. 01 — THE KEVLAR SHIELD</span>
    </div>
  );
}

function HomepageHeader() {
  return (
    <header className={styles.hero}>
      <div className="container">
        <div className={styles.heroInner}>
          <div className={styles.heroText}>
            <span className={styles.eyebrow}><span /> FAST RESILIENCE FOR .NET</span>
            <Heading as="h1" className={styles.heroTitle}>Built to<br />take a <em>hit.</em></Heading>
            <p className={styles.heroTagline}>Dependencies fail. Your application doesn’t have to. Wrap every call in a little Kevlar.</p>
            <p className={styles.heroDescription}>Retries, circuit breakers, timeouts and more.<br />Seven strategies. One fluent shield API.</p>
            <div className={styles.buttons}>
              <Link className={styles.primaryButton} to="/docs/getting-started">Build your first shield <ArrowIcon /></Link>
              <Link className={styles.secondaryButton} href="https://github.com/thomhurst/Kevlar">View on GitHub <ArrowIcon /></Link>
            </div>
            <div className={styles.installLine}><span aria-hidden="true">$</span><code>dotnet add package Kevlar</code></div>
          </div>
          <ShieldIllustration />
        </div>
        <div className={styles.heroFootnote}>
          <span>STRONG PROTECTION. LIGHT FOOTPRINT.</span>
          <div><span>NativeAOT ready</span><span>Allocation-conscious</span><span>Open source</span></div>
        </div>
      </div>
    </header>
  );
}

function Strategies() {
  return (
    <section className={styles.strategies} aria-labelledby="strategies-title">
      <div className="container">
        <div className={styles.sectionIntro}>
          <div><span className={styles.kicker}>LAYERS OF RESILIENCE</span><Heading as="h2" id="strategies-title">Whatever breaks.<br />There’s a strategy for that.</Heading></div>
          <p>Pick the protection your dependency needs. Chain strategies together into one reusable shield.</p>
        </div>
        <div className={styles.strategyGrid}>
          {strategies.map(({ title, slug, symbol, description }) => (
            <Link key={slug} className={styles.strategyCard} to={`/docs/strategies/${slug}`}>
              <span className={styles.strategyIcon} aria-hidden="true">{symbol}</span>
              <Heading as="h3">{title}</Heading><p>{description}</p><ArrowIcon />
            </Link>
          ))}
          <Link className={`${styles.strategyCard} ${styles.composeCard}`} to="/docs/composition">
            <span className={styles.strategyIcon} aria-hidden="true">+</span>
            <Heading as="h3">Stronger together.</Heading><p>Compose your own protection.</p><ArrowIcon />
          </Link>
        </div>
      </div>
    </section>
  );
}

function CodeShowcase() {
  return (
    <section className={styles.codeSection} aria-labelledby="code-title">
      <div className={`container ${styles.codeGrid}`}>
        <div className={styles.codeIntro}>
          <span className={styles.kicker}>LESS CEREMONY. MORE CONFIDENCE.</span>
          <Heading as="h2" id="code-title">Looks like code.<br />Works like armor.</Heading>
          <p>Build a shield once. Reuse it across calls. Protection reads in execution order, from the outside in.</p>
          <ol className={styles.pipelineSteps}>
            <li><span>01</span><div><strong>Set the budget</strong><p>Timeout bounds the whole operation.</p></div></li>
            <li><span>02</span><div><strong>Handle the unexpected</strong><p>Retry adds backoff and jitter by default.</p></div></li>
            <li><span>03</span><div><strong>Give failures a limit</strong><p>The circuit breaker stops repeated failures.</p></div></li>
          </ol>
          <Link className={styles.textLink} to="/docs/getting-started">Walk through the quickstart <ArrowIcon /></Link>
        </div>
        <div className={styles.codeWindow}>
          <div className={styles.codeBar}><span><i /> YourFirstShield.cs</span><span>C#</span></div>
          <CodeBlock language="csharp">{shieldSample}</CodeBlock>
          <div className={styles.codeFooter}><span>Timeout <b>›</b> Retry <b>›</b> Circuit breaker</span><span>ONE SHIELD</span></div>
        </div>
      </div>
    </section>
  );
}

function Features() {
  return (
    <section className={styles.features} aria-labelledby="features-title">
      <div className="container">
        <span className={styles.kicker}>STRENGTH WITHOUT THE WEIGHT</span>
        <Heading as="h2" id="features-title">Heavy on resilience. Light on everything else.</Heading>
        <div className={styles.featureGrid}>
          {features.map(({ label, title, description, link, linkLabel }) => (
            <article className={styles.feature} key={label}>
              <span className={styles.featureLabel}>{label}</span><Heading as="h3">{title}</Heading><p>{description}</p>
              <Link className={styles.textLink} to={link}>{linkLabel} <ArrowIcon /></Link>
            </article>
          ))}
        </div>
      </div>
    </section>
  );
}

function FinalCta() {
  const logoUrl = useBaseUrl('/img/logo.svg');
  return (
    <section className={styles.finalCta} aria-labelledby="cta-title">
      <div className={`container ${styles.ctaInner}`}>
        <img src={logoUrl} width="64" height="64" alt="" />
        <div><span className={styles.eyebrow}>READY FOR THE REAL WORLD</span><Heading as="h2" id="cta-title">Give your code a fighting chance.</Heading></div>
        <Link className={styles.primaryButton} to="/docs/getting-started">Get started with Kevlar <ArrowIcon /></Link>
      </div>
    </section>
  );
}

export default function Home(): ReactNode {
  return (
    <Layout title="Fast resilience for .NET" description="Built to take a hit. Retries, circuit breakers, timeouts and more — seven resilience strategies, one fluent Kevlar shield API for .NET.">
      <main className={styles.main}>
        <HomepageHeader />
        <Strategies />
        <CodeShowcase />
        <Features />
        <FinalCta />
      </main>
    </Layout>
  );
}
