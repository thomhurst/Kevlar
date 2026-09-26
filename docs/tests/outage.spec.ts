import { expect, test } from '@playwright/test';

for (const width of [1440, 390]) {
  test(`outage report and matching JSON load at ${width}px`, async ({ page, request }) => {
    await page.setViewportSize({ width, height: 900 });
    await page.goto('docs/outage-recovery/');
    await expect(page.getByRole('heading', { name: 'Controlled outage and recovery', exact: true })).toBeVisible();
    const href = await page.getByRole('link', { name: 'Current published JSON', exact: true }).getAttribute('href');
    expect(href).toBeTruthy();
    const response = await request.get(href!);
    expect(response.ok()).toBe(true);
    const data = await response.json();
    expect(data.schemaVersion).toBe(1);
    expect(data.scenarios).toHaveLength(4);
    await expect(page.locator('article')).toContainText(data.commit);
    for (const scenario of data.scenarios) {
      expect(scenario.activeAfterDrain).toBe(0);
      for (const phase of scenario.phases) {
        expect(phase.succeeded + phase.failed).toBe(phase.offered);
      }
    }
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
  });
}
