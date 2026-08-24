import { expect, test } from '@playwright/test';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';

test('home page recommends the Kevlar timeout exception', async ({ page }) => {
  const homePage = await readFile(resolve('build/index.html'), 'utf8');
  await page.setContent(homePage);

  const timeoutClause = page
    .locator('code')
    .filter({ hasText: 'Shield.When<TimeoutExceededException>().Retry(3)' });

  await expect(timeoutClause).toHaveCount(1);
  await expect(timeoutClause).toMatchAriaSnapshot(`
    - code: Shield.When<TimeoutExceededException>().Retry(3)
  `);
  await expect(page.locator('main')).not.toContainText('When<TimeoutException>');
});
