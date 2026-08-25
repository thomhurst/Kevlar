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
