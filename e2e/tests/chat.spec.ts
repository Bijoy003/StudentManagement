import { test, expect, login } from './fixtures';

const PixelPng = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9Zl1sAAAAASUVORK5CYII=',
  'base64',
);

async function gotoChat(page: import('@playwright/test').Page): Promise<void> {
  await login(page);
  await page.goto('/Chat');
  await expect(page.getByRole('heading', { name: 'Chat Assistant' })).toBeVisible();
}

test.describe('Chat', () => {
  test('Chat page loads and exposes message input', async ({ page }) => {
    await gotoChat(page);
    await expect(page.locator('#chat-input')).toBeVisible();
    await expect(page.locator('#chat-send')).toBeEnabled();
  });

  test('Rejects unsupported file type with toastr error', async ({ page }) => {
    await gotoChat(page);

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
    await gotoChat(page);

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
    await gotoChat(page);

    await page.locator('#chat-send').click();

    await expect(page.locator('.chat-message')).toHaveCount(0);
    await expect(page.locator('#chat-input')).toBeEnabled();
    await expect(page.locator('#chat-send')).toBeEnabled();
  });

  test('Sends message and renders assistant reply', async ({ page }) => {
    await gotoChat(page);
    await page.route('**/Chat/Send', route =>
      route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ reply: 'Hello back from the assistant.' }) }));

    await page.locator('#chat-input').fill('Hi there');
    await page.locator('#chat-send').click();

    await expect(page.locator('.chat-message')).toHaveCount(2);
    await expect(page.locator('.chat-bubble-user')).toContainText('Hi there');
    await expect(page.locator('.chat-markdown')).toContainText('Hello back from the assistant.');
    await expect(page.locator('#chat-send')).toBeEnabled();
  });

  test('Enter key sends the message', async ({ page }) => {
    await gotoChat(page);
    await page.route('**/Chat/Send', route =>
      route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ reply: 'Received.' }) }));

    await page.locator('#chat-input').fill('Sent with Enter');
    await page.locator('#chat-input').press('Enter');

    await expect(page.locator('.chat-message')).toHaveCount(2);
    await expect(page.locator('.chat-bubble-user')).toContainText('Sent with Enter');
  });

  test('Sends the accumulated conversation history', async ({ page }) => {
    await gotoChat(page);
    const payloads: Array<{ messages: Array<{ role: string; text: string }> }> = [];
    await page.route('**/Chat/Send', async route => {
      payloads.push(route.request().postDataJSON());
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ reply: `reply ${payloads.length}` }),
      });
    });

    await page.locator('#chat-input').fill('first');
    await page.locator('#chat-send').click();
    await expect(page.locator('.chat-message')).toHaveCount(2);

    await page.locator('#chat-input').fill('second');
    await page.locator('#chat-send').click();
    await expect(page.locator('.chat-message')).toHaveCount(4);

    expect(payloads).toHaveLength(2);
    expect(payloads[0].messages).toHaveLength(1);
    expect(payloads[1].messages.map(m => m.role)).toEqual(['user', 'assistant', 'user']);
    expect(payloads[1].messages.map(m => m.text)).toEqual(['first', 'reply 1', 'second']);
  });

  test('Service unavailable pops the failed message and shows error', async ({ page }) => {
    await gotoChat(page);
    let call = 0;
    let secondPayload: { messages: unknown[] } | null = null;
    await page.route('**/Chat/Send', async route => {
      call++;
      if (call === 1) {
        await route.fulfill({
          status: 503,
          contentType: 'application/json',
          body: JSON.stringify({ error: 'The chat service is currently unavailable.' }),
        });
        return;
      }

      secondPayload = route.request().postDataJSON();
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ reply: 'recovered' }),
      });
    });

    await page.locator('#chat-input').fill('doomed message');
    await page.locator('#chat-send').click();
    await expect(page.locator('#toast-container')).toContainText('currently unavailable');
    await expect(page.locator('#chat-send')).toBeEnabled();

    await page.locator('#chat-input').fill('retry message');
    await page.locator('#chat-send').click();
    await expect(page.locator('.chat-markdown')).toContainText('recovered');

    expect(secondPayload).not.toBeNull();
    expect(secondPayload!.messages).toHaveLength(1);
  });

  test('Network failure shows unavailable toast and re-enables input', async ({ page }) => {
    await gotoChat(page);
    await page.route('**/Chat/Send', route => route.abort('failed'));

    await page.locator('#chat-input').fill('hello');
    await page.locator('#chat-send').click();

    await expect(page.locator('#toast-container')).toContainText('currently unavailable');
    await expect(page.locator('#chat-input')).toBeEnabled();
    await expect(page.locator('#chat-send')).toBeEnabled();
  });

  test('Removes an attached file from the preview', async ({ page }) => {
    await gotoChat(page);
    await page.locator('#chat-file-input').setInputFiles({
      name: 'notes.txt',
      mimeType: 'text/plain',
      buffer: Buffer.from('hello', 'utf8'),
    });
    await expect(page.locator('#chat-file-preview')).toBeVisible();

    await page.locator('#chat-file-preview .btn-close').click();

    await expect(page.locator('#chat-file-preview')).toBeHidden();
    await expect(page.locator('#chat-file-input')).toHaveValue('');
  });

  test('Sends an image attachment and renders the preview', async ({ page }) => {
    await gotoChat(page);
    const payloads: Array<{ messages: Array<{ fileName?: string; fileData?: string }> }> = [];
    await page.route('**/Chat/Send', async route => {
      payloads.push(route.request().postDataJSON());
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ reply: 'Nice image.' }),
      });
    });

    await page.locator('#chat-file-input').setInputFiles({
      name: 'pixel.png',
      mimeType: 'image/png',
      buffer: PixelPng,
    });
    await expect(page.locator('#chat-file-preview .file-name')).toHaveText('pixel.png');

    await page.locator('#chat-input').fill('What is this?');
    await page.locator('#chat-send').click();

    await expect(page.locator('.chat-message')).toHaveCount(2);
    const image = page.locator('.chat-bubble-user .chat-image img');
    await expect(image).toBeVisible();
    await expect(image).toHaveAttribute('src', /^data:image\/png;base64,/);

    expect(payloads[0].messages[0].fileName).toBe('pixel.png');
    expect(payloads[0].messages[0].fileData).toBeTruthy();
  });

  test('Escapes HTML in user messages', async ({ page }) => {
    await gotoChat(page);
    await page.route('**/Chat/Send', route =>
      route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ reply: 'ok' }) }));

    await page.locator('#chat-input').fill('<img src=x onerror="window.__chatXss = 1">');
    await page.locator('#chat-send').click();
    await expect(page.locator('.chat-message')).toHaveCount(2);

    await expect(page.locator('.chat-bubble-user img')).toHaveCount(0);
    expect(await page.evaluate(() => (window as unknown as { __chatXss?: number }).__chatXss)).toBeUndefined();
  });

  test('Sanitizes assistant markdown output', async ({ page }) => {
    await gotoChat(page);
    await page.route('**/Chat/Send', route =>
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          reply: '<img src=x onerror="window.__chatXss = 1"><script>window.__chatXss = 2</script>safe text',
        }),
      }));

    await page.locator('#chat-input').fill('show me html');
    await page.locator('#chat-send').click();
    await expect(page.locator('.chat-markdown')).toContainText('safe text');

    await expect(page.locator('.chat-markdown script')).toHaveCount(0);
    await expect(page.locator('.chat-markdown [onerror]')).toHaveCount(0);
    expect(await page.evaluate(() => (window as unknown as { __chatXss?: number }).__chatXss)).toBeUndefined();
  });

  test('Reloading the page clears the conversation', async ({ page }) => {
    await gotoChat(page);
    await page.route('**/Chat/Send', route =>
      route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ reply: 'transient' }) }));

    await page.locator('#chat-input').fill('hello');
    await page.locator('#chat-send').click();
    await expect(page.locator('.chat-message')).toHaveCount(2);

    await page.reload();

    await expect(page.locator('.chat-message')).toHaveCount(0);
  });

  test('Anonymous visitor is redirected to login', async ({ page }) => {
    await page.goto('/Chat');

    await expect(page).toHaveURL(/\/Account\/Login/);
  });

  test('Real provider smoke test', async ({ page }) => {
    test.skip(!process.env.CHAT_SMOKE, 'Set CHAT_SMOKE=1 to run against the configured provider.');

    await gotoChat(page);
    await page.locator('#chat-input').fill('Say hello in one short sentence.');
    await page.locator('#chat-send').click();

    await expect(page.locator('.chat-markdown')).not.toBeEmpty({ timeout: 60_000 });
    await expect(page.locator('#chat-send')).toBeEnabled({ timeout: 60_000 });
  });
});
