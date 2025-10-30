import { test, expect } from '@playwright/test';

test.describe('Contract Management', () => {
  test.beforeEach(async ({ page }) => {
    // Login as test customer
    await page.goto('/login');
    await page.fill('[name="email"]', 'test@example.com');
    await page.fill('[name="password"]', 'TestPassword123!');
    await page.click('button[type="submit"]');
    
    // Wait for redirect to dashboard
    await page.waitForURL('/dashboard');
  });

  test('should display customer contracts', async ({ page }) => {
    await page.goto('/contracts');

    // Wait for contracts to load
    await expect(page.locator('h2').filter({ hasText: 'My Contracts' })).toBeVisible();

    // Check that at least one contract is displayed
    const contracts = page.locator('[data-testid="contract-card"]');
    await expect(contracts.first()).toBeVisible();
  });

  test('should toggle auto-renew', async ({ page }) => {
    await page.goto('/contracts');

    // Find active contract
    const activeContract = page.locator('[data-testid="contract-card"]').filter({
      has: page.locator('text=Status: active')
    }).first();

    // Get current auto-renew state
    const checkbox = activeContract.locator('input[type="checkbox"]');
    const initialState = await checkbox.isChecked();

    // Toggle auto-renew
    await checkbox.click();

    // Verify state changed
    await expect(checkbox).toBeChecked({ checked: !initialState });

    // Refresh page and verify persistence
    await page.reload();
    await expect(checkbox).toBeChecked({ checked: !initialState });
  });

  test('should show contract details', async ({ page }) => {
    await page.goto('/contracts');

    // Click first contract
    await page.locator('[data-testid="contract-card"]').first().click();

    // Verify detail page loaded
    await expect(page.locator('h1').filter({ hasText: /Contract/ })).toBeVisible();
    
    // Verify contract data is displayed
    await expect(page.locator('text=/Start Date:/i')).toBeVisible();
    await expect(page.locator('text=/End Date:/i')).toBeVisible();
    await expect(page.locator('text=/Duration:/i')).toBeVisible();
  });
});
