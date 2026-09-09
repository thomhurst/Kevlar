import { expect, test } from '@playwright/test';

test('API navigation and a representative type page load', async ({ page }) => {
  await page.goto('./');

  const apiLink = page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: /^API/ });
  await expect(apiLink).toBeVisible();
  const apiHref = await apiLink.getAttribute('href');
  expect(apiHref).toBe('/Kevlar/api/index.html');
  await page.goto('http://127.0.0.1:3001/index.html');
  await expect(page).toHaveURL(/:3001\/index\.html$/);

  await page.goto('http://127.0.0.1:3001/Kevlar.Shield.html');
  await expect(page.getByRole('heading', { name: /^Class Shield/ })).toBeVisible();
  await expect(page.getByRole('heading', { name: /Retry\(/ }).first()).toBeVisible();
});

for (const type of [
  'Kevlar.Shield',
  'Kevlar.Shield-1',
  'Kevlar.Testing.ShieldDescriptor',
  'Microsoft.Extensions.DependencyInjection.KevlarServiceCollectionExtensions',
]) {
  test(`API links resolve from ${type}`, async ({ page, request }) => {
    const response = await page.goto(`http://127.0.0.1:3001/${type}.html`);
    expect(response?.status()).toBe(200);

    const links = await page.locator('article a[href]').evaluateAll(anchors =>
      [...new Set(anchors.map(anchor => (anchor as HTMLAnchorElement).href))],
    );
    expect(links.length).toBeGreaterThan(0);

    for (const link of links) {
      const url = new URL(link);
      expect(url.href).not.toMatch(/^https:\/\/learn\.microsoft\.com\/.*\/kevlar[./]/i);
      if (url.origin === 'http://127.0.0.1:3001' && url.pathname.endsWith('.html')) {
        expect((await request.get(url.href)).status(), link).toBe(200);
      }
    }
  });
}
