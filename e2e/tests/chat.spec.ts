import { test, expect, login } from './fixtures';

test.describe('Chat', () => {
  test('Chat page loads and exposes message input', async ({ page }) => {
    await login(page);
    await page.goto('/Chat');
    await expect(page.getByRole('heading', { name: 'Chat Assistant' })).toBeVisible();
    await expect(page.locator('#chat-input')).toBeVisible();
    await expect(page.locator('#chat-send')).toBeEnabled();
  });

  test('Rejects unsupported file type with toastr error', async ({ page }) => {
    await login(page);
    await page.goto('/Chat');

    await page.locator('#chat-file-input').setInputFiles({
      name: 'malware.exe',
      mimeType: 'application/x-msdownload',
      buffer: Buffer.from('MZ...', 'utf8'),
    });

    await expect(page.locator('#toast-container')).toContainText('Unsupported file type', {
      timeout: 5_000,
    });
    await expect(page.locator('#chat-file-preview')).toBeHidden();
  });

  test('Accepts text file and shows preview', async ({ page }) => {
    await login(page);
    await page.goto('/Chat');

    await page.locator('#chat-file-input').setInputFiles({
      name: 'notes.txt',
      mimeType: 'text/plain',
      buffer: Buffer.from('hello', 'utf8'),
    });

    await expect(page.locator('#chat-file-preview .file-name')).toHaveText('notes.txt', {
      timeout: 5_000,
    });
    await expect(page.locator('#chat-file-preview')).toBeVisible();
  });

  test('Empty message does not send or disable input', async ({ page }) => {
    await login(page);
    await page.goto('/Chat');

    await page.locator('#chat-send').click();

    await expect(page.locator('.chat-message')).toHaveCount(0);
    await expect(page.locator('#chat-input')).toBeEnabled();
    await expect(page.locator('#chat-send')).toBeEnabled();
  });
});
