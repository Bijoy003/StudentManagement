import { test, expect, login, loginAs } from './fixtures';

const PixelPng = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9Zl1sAAAAASUVORK5CYII=',
  'base64',
);

type ChatPayload = {
  messages: Array<{ role: string; text: string; fileName?: string; fileData?: string }>;
};

function sseBody(events: Array<Record<string, unknown>>): string {
  return events.map(e => `data: ${JSON.stringify(e)}\n\n`).join('');
}

function replyEvents(
  text: string,
  extra?: { elapsedMs?: number; inputTokens?: number; outputTokens?: number; quota?: unknown },
): Array<Record<string, unknown>> {
  return [
    { type: 'status', status: 'Generating reply…' },
    { type: 'delta', text },
    {
      type: 'done',
      elapsedMs: extra?.elapsedMs ?? 120,
      inputTokens: extra?.inputTokens ?? 10,
      outputTokens: extra?.outputTokens ?? 5,
      quota: extra?.quota ?? null,
    },
  ];
}

function fulfillStream(events: Array<Record<string, unknown>>) {
  return { status: 200, contentType: 'text/event-stream', body: sseBody(events) };
}

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
    await expect(page.locator('#chat-quota')).toBeVisible();
    await expect(page.locator('#chat-capability-hint')).not.toBeEmpty();
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

  test('Sends message and renders streamed assistant reply', async ({ page }) => {
    await gotoChat(page);
    await page.route('**/Chat/SendStream', route =>
      route.fulfill(fulfillStream(replyEvents('Hello back from the assistant.'))));

    await page.locator('#chat-input').fill('Hi there');
    await page.locator('#chat-send').click();

    await expect(page.locator('.chat-message')).toHaveCount(2);
    await expect(page.locator('.chat-bubble-user')).toContainText('Hi there');
    await expect(page.locator('.chat-markdown')).toContainText('Hello back from the assistant.');
    await expect(page.locator('#chat-send')).toBeEnabled();
  });

  test('Streamed reply shows elapsed time and token footer', async ({ page }) => {
    await gotoChat(page);
    await page.route('**/Chat/SendStream', route =>
      route.fulfill(fulfillStream(replyEvents('Timed reply.', { elapsedMs: 1234, inputTokens: 10, outputTokens: 5 }))));

    await page.locator('#chat-input').fill('How long?');
    await page.locator('#chat-send').click();

    await expect(page.locator('.chat-meta')).toContainText('1.2s');
    await expect(page.locator('.chat-meta')).toContainText('15 tokens');
  });

  test('Done event updates the quota indicator', async ({ page }) => {
    await gotoChat(page);
    await page.route('**/Chat/SendStream', route =>
      route.fulfill(fulfillStream(replyEvents('Counted.', {
        quota: { used: 42, limit: 20000, unlimited: false, remaining: 19958 },
      }))));

    await page.locator('#chat-input').fill('Count me');
    await page.locator('#chat-send').click();

    await expect(page.locator('#chat-quota-used')).toHaveText('42');
  });

  test('Enter key sends the message', async ({ page }) => {
    await gotoChat(page);
    await page.route('**/Chat/SendStream', route =>
      route.fulfill(fulfillStream(replyEvents('Received.'))));

    await page.locator('#chat-input').fill('Sent with Enter');
    await page.locator('#chat-input').press('Enter');

    await expect(page.locator('.chat-message')).toHaveCount(2);
    await expect(page.locator('.chat-bubble-user')).toContainText('Sent with Enter');
  });

  test('Sends the accumulated conversation history', async ({ page }) => {
    await gotoChat(page);
    const payloads: ChatPayload[] = [];
    await page.route('**/Chat/SendStream', async route => {
      payloads.push(route.request().postDataJSON());
      await route.fulfill(fulfillStream(replyEvents(`reply ${payloads.length}`)));
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

  test('Failed request keeps the message and Retry resends it', async ({ page }) => {
    await gotoChat(page);
    let call = 0;
    const payloads: ChatPayload[] = [];
    await page.route('**/Chat/SendStream', async route => {
      call++;
      payloads.push(route.request().postDataJSON());
      if (call === 1) {
        await route.fulfill({
          status: 503,
          contentType: 'application/json',
          body: JSON.stringify({ error: 'The chat service is currently unavailable.' }),
        });
        return;
      }
      await route.fulfill(fulfillStream(replyEvents('recovered')));
    });

    await page.locator('#chat-input').fill('doomed message');
    await page.locator('#chat-send').click();

    await expect(page.locator('#toast-container')).toContainText('currently unavailable');
    await expect(page.locator('.chat-failure-notice')).toBeVisible();
    // The user message is kept, not popped.
    await expect(page.locator('.chat-bubble-user')).toContainText('doomed message');

    await page.locator('.chat-retry-btn').click();
    await expect(page.locator('.chat-markdown')).toContainText('recovered');
    await expect(page.locator('.chat-failure-notice')).toHaveCount(0);

    expect(payloads).toHaveLength(2);
    expect(payloads[1].messages).toEqual(payloads[0].messages);
  });

  test('Mid-stream error event offers retry with a clean history', async ({ page }) => {
    await gotoChat(page);
    let call = 0;
    const payloads: ChatPayload[] = [];
    await page.route('**/Chat/SendStream', async route => {
      call++;
      payloads.push(route.request().postDataJSON());
      if (call === 1) {
        await route.fulfill(fulfillStream([
          { type: 'status', status: 'Generating reply…' },
          { type: 'delta', text: 'partial…' },
          { type: 'error', error: 'The provider stopped responding.' },
        ]));
        return;
      }
      await route.fulfill(fulfillStream(replyEvents('recovered')));
    });

    await page.locator('#chat-input').fill('interrupted');
    await page.locator('#chat-send').click();

    await expect(page.locator('.chat-failure-notice')).toBeVisible();
    // The partial assistant turn is discarded so retry ends with the user message.
    await expect(page.locator('.chat-bubble-assistant')).toHaveCount(0);

    await page.locator('.chat-retry-btn').click();
    await expect(page.locator('.chat-markdown')).toContainText('recovered');

    expect(payloads[1].messages.map(m => m.role)).toEqual(['user']);
  });

  test('Quota exceeded shows 429 message and marks the indicator', async ({ page }) => {
    await gotoChat(page);
    await page.route('**/Chat/SendStream', route =>
      route.fulfill({
        status: 429,
        contentType: 'application/json',
        body: JSON.stringify({
          error: 'Daily chat token limit reached. Your quota resets at midnight UTC.',
          used: 20000,
          limit: 20000,
        }),
      }));

    await page.locator('#chat-input').fill('over budget');
    await page.locator('#chat-send').click();

    await expect(page.locator('#toast-container')).toContainText('Daily chat token limit reached');
    await expect(page.locator('#chat-quota')).toHaveClass(/text-danger/);
    await expect(page.locator('.chat-failure-notice')).toBeVisible();
  });

  test('Network failure shows unavailable toast and re-enables input', async ({ page }) => {
    await gotoChat(page);
    await page.route('**/Chat/SendStream', route => route.abort('failed'));

    await page.locator('#chat-input').fill('hello');
    await page.locator('#chat-send').click();

    await expect(page.locator('#toast-container')).toContainText('currently unavailable');
    await expect(page.locator('#chat-input')).toBeEnabled();
    await expect(page.locator('#chat-send')).toBeEnabled();
  });

  test('Editing a previous message truncates history and resends', async ({ page }) => {
    await gotoChat(page);
    const payloads: ChatPayload[] = [];
    await page.route('**/Chat/SendStream', async route => {
      payloads.push(route.request().postDataJSON());
      await route.fulfill(fulfillStream(replyEvents(`reply ${payloads.length}`)));
    });

    await page.locator('#chat-input').fill('first question');
    await page.locator('#chat-send').click();
    await expect(page.locator('.chat-message')).toHaveCount(2);

    await page.locator('#chat-input').fill('second question');
    await page.locator('#chat-send').click();
    await expect(page.locator('.chat-message')).toHaveCount(4);

    page.on('dialog', dialog => dialog.accept());
    await page.locator('.chat-message[data-history-index="0"] .chat-edit-btn').click();
    await page.locator('.chat-message[data-history-index="0"] textarea').fill('edited question');
    await page.locator('.chat-edit-save').click();

    await expect(page.locator('.chat-message')).toHaveCount(2);
    await expect(page.locator('.chat-bubble-user')).toContainText('edited question');
    await expect(page.locator('.chat-markdown')).toContainText('reply 3');

    expect(payloads).toHaveLength(3);
    expect(payloads[2].messages).toHaveLength(1);
    expect(payloads[2].messages[0].text).toBe('edited question');
  });

  test('Cancelling an edit keeps the transcript intact', async ({ page }) => {
    await gotoChat(page);
    await page.route('**/Chat/SendStream', route =>
      route.fulfill(fulfillStream(replyEvents('kept reply'))));

    await page.locator('#chat-input').fill('original');
    await page.locator('#chat-send').click();
    await expect(page.locator('.chat-message')).toHaveCount(2);

    await page.locator('.chat-message[data-history-index="0"] .chat-edit-btn').click();
    await page.locator('.chat-message[data-history-index="0"] textarea').fill('discarded edit');
    await page.locator('.chat-edit-cancel').click();

    await expect(page.locator('.chat-bubble-user')).toContainText('original');
    await expect(page.locator('.chat-markdown')).toContainText('kept reply');
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
    const payloads: ChatPayload[] = [];
    await page.route('**/Chat/SendStream', async route => {
      payloads.push(route.request().postDataJSON());
      await route.fulfill(fulfillStream(replyEvents('Nice image.')));
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
    await page.route('**/Chat/SendStream', route =>
      route.fulfill(fulfillStream(replyEvents('ok'))));

    await page.locator('#chat-input').fill('<img src=x onerror="window.__chatXss = 1">');
    await page.locator('#chat-send').click();
    await expect(page.locator('.chat-message')).toHaveCount(2);

    await expect(page.locator('.chat-bubble-user img')).toHaveCount(0);
    expect(await page.evaluate(() => (window as unknown as { __chatXss?: number }).__chatXss)).toBeUndefined();
  });

  test('Sanitizes assistant markdown output', async ({ page }) => {
    await gotoChat(page);
    await page.route('**/Chat/SendStream', route =>
      route.fulfill(fulfillStream(replyEvents(
        '<img src=x onerror="window.__chatXss = 1"><script>window.__chatXss = 2</script>safe text',
      ))));

    await page.locator('#chat-input').fill('show me html');
    await page.locator('#chat-send').click();
    await expect(page.locator('.chat-markdown')).toContainText('safe text');

    await expect(page.locator('.chat-markdown script')).toHaveCount(0);
    await expect(page.locator('.chat-markdown [onerror]')).toHaveCount(0);
    expect(await page.evaluate(() => (window as unknown as { __chatXss?: number }).__chatXss)).toBeUndefined();
  });

  test('Reloading the page clears the conversation', async ({ page }) => {
    await gotoChat(page);
    await page.route('**/Chat/SendStream', route =>
      route.fulfill(fulfillStream(replyEvents('transient'))));

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

test.describe('Chat admin settings', () => {
  test('Admin can save chat settings and they persist', async ({ page }) => {
    await login(page);
    await page.goto('/Settings/Settings');
    await expect(page.getByRole('heading', { name: 'Chat Settings' })).toBeVisible();

    const rag = page.locator('#IncludeRag');
    const original = await rag.isChecked();
    await rag.setChecked(!original);
    await page.getByRole('button', { name: 'Save Settings' }).click();

    await expect(page.locator('.alert-success')).toContainText('Settings saved.');
    await page.reload();
    expect(await page.locator('#IncludeRag').isChecked()).toBe(!original);

    // Restore so other tests see the baseline configuration.
    await page.locator('#IncludeRag').setChecked(original);
    await page.getByRole('button', { name: 'Save Settings' }).click();
    await expect(page.locator('.alert-success')).toContainText('Settings saved.');
  });

  test('Admin error log page loads', async ({ page }) => {
    await login(page);
    await page.goto('/Settings/Errors');

    await expect(page.getByRole('heading', { name: 'Error Log' })).toBeVisible();
    const hasErrors = await page.locator('table tbody tr').count();
    if (hasErrors === 0) {
      await expect(page.locator('.alert-success')).toContainText('No errors recorded');
    }
  });

  test('Non-admin cannot open chat settings', async ({ page }) => {
    await loginAs(page, 'teacher@teacher.com', 'Teacher123');
    await page.goto('/Settings/Settings');

    await expect(page).toHaveURL(/\/Account\/AccessDenied/);
  });
});