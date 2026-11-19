import { test, expect, login, loginAs, logout } from './fixtures';
import type { Page } from '@playwright/test';

type ChatPayload = {
  messages: Array<{ role: string; text: string; fileName?: string; fileData?: string }>;
};

// Same 1x1 transparent PNG used by chat.spec.ts, as the base64 payload FileReader produces.
const PixelPngBase64 =
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9Zl1sAAAAASUVORK5CYII=';

function sseBody(events: Array<Record<string, unknown>>): string {
  return events.map(e => `data: ${JSON.stringify(e)}\n\n`).join('');
}

function replyEvents(text: string): Array<Record<string, unknown>> {
  return [
    { type: 'status', status: 'Generating reply…' },
    { type: 'delta', text },
    { type: 'done', elapsedMs: 120, inputTokens: 10, outputTokens: 5, quota: null },
  ];
}

function fulfillStream(events: Array<Record<string, unknown>>) {
  return { status: 200, contentType: 'text/event-stream', body: sseBody(events) };
}

/** Opens the widget panel if it is not already open (open state persists across pages). */
async function openWidget(page: Page): Promise<void> {
  const panel = page.locator('#chat-widget-panel');
  if (await panel.isHidden()) {
    await page.locator('#chat-widget-toggle').click();
  }
  await expect(panel).toBeVisible();
}

async function sendWidgetMessage(page: Page, text: string): Promise<void> {
  await page.locator('#chat-input').fill(text);
  await page.locator('#chat-send').click();
}

test.describe('Chat widget', () => {
  test('Widget toggle is visible on Home and panel starts hidden with chat markup', async ({ page }) => {
    await login(page);

    const toggle = page.locator('#chat-widget-toggle');
    const panel = page.locator('#chat-widget-panel');
    await expect(toggle).toBeVisible();
    await expect(panel).toBeHidden();

    // Panel is a chat root scoped to the current user, with its own antiforgery token.
    expect(await panel.getAttribute('data-chat-root')).not.toBeNull();
    await expect(panel).toHaveAttribute('data-user-id', /.+/);
    await expect(panel.locator('input[name="__RequestVerificationToken"]')).toHaveCount(1);

    // Same element ids as the full chat page, inside the panel.
    const quota = panel.locator('#chat-quota');
    expect(await quota.getAttribute('data-used')).not.toBeNull();
    expect(await quota.getAttribute('data-limit')).not.toBeNull();
    expect(await quota.getAttribute('data-unlimited')).not.toBeNull();
    await expect(quota.locator('#chat-quota-used')).toHaveCount(1);
    for (const id of ['#chat-messages', '#chat-loading', '#chat-status-text', '#chat-file-preview', '#chat-input', '#chat-file-btn', '#chat-file-input', '#chat-send', '#chat-widget-close']) {
      await expect(panel.locator(id)).toHaveCount(1);
    }
  });

  test('Anonymous visitor sees no widget on the login page', async ({ page }) => {
    await page.goto('/Account/Login');

    await expect(page.locator('#chat-widget-toggle')).toHaveCount(0);
    await expect(page.locator('#chat-widget-panel')).toHaveCount(0);
  });

  test('Chat page has no widget toggle but keeps the full chat UI', async ({ page }) => {
    await login(page);
    await page.goto('/Chat');

    await expect(page.locator('#chat-widget-toggle')).toHaveCount(0);
    await expect(page.locator('#chat-input')).toBeVisible();

    const container = page.locator('.chat-container');
    expect(await container.getAttribute('data-chat-root')).not.toBeNull();
    await expect(container).toHaveAttribute('data-user-id', /.+/);
  });

  test('Opens panel and sends a message with streamed reply inside the widget', async ({ page }) => {
    await login(page);
    const requests: Array<{ payload: ChatPayload; token: string | undefined }> = [];
    await page.route('**/Chat/SendStream', async route => {
      requests.push({
        payload: route.request().postDataJSON() as ChatPayload,
        token: route.request().headers()['requestverificationtoken'],
      });
      await route.fulfill(fulfillStream(replyEvents('Widget reply.')));
    });

    await openWidget(page);
    await sendWidgetMessage(page, 'Hi from the widget');

    const panel = page.locator('#chat-widget-panel');
    await expect(panel.locator('.chat-message')).toHaveCount(2);
    await expect(panel.locator('.chat-bubble-user')).toContainText('Hi from the widget');
    await expect(panel.locator('.chat-markdown')).toContainText('Widget reply.');
    await expect(page.locator('#chat-send')).toBeEnabled();

    // The widget posts to the same endpoint with the panel-scoped antiforgery token.
    expect(requests).toHaveLength(1);
    expect(requests[0].token).toBeTruthy();
    expect(requests[0].payload.messages.map(m => m.text)).toEqual(['Hi from the widget']);
  });

  test('Close button hides the panel and reopening keeps the transcript', async ({ page }) => {
    await login(page);
    await page.route('**/Chat/SendStream', route =>
      route.fulfill(fulfillStream(replyEvents('Kept reply.'))));

    await openWidget(page);
    await sendWidgetMessage(page, 'remember me');
    await expect(page.locator('#chat-widget-panel .chat-message')).toHaveCount(2);

    await page.locator('#chat-widget-close').click();
    await expect(page.locator('#chat-widget-panel')).toBeHidden();

    await page.locator('#chat-widget-toggle').click();
    await expect(page.locator('#chat-widget-panel')).toBeVisible();
    await expect(page.locator('#chat-widget-panel .chat-message')).toHaveCount(2);
    await expect(page.locator('#chat-widget-panel .chat-bubble-user')).toContainText('remember me');
    await expect(page.locator('#chat-widget-panel .chat-markdown')).toContainText('Kept reply.');
  });

  test('Escape closes the open panel', async ({ page }) => {
    await login(page);

    await openWidget(page);
    await page.keyboard.press('Escape');

    await expect(page.locator('#chat-widget-panel')).toBeHidden();
    expect(await page.evaluate(() => sessionStorage.getItem('sm.chat.widgetOpen'))).toBe('0');
  });

  test('Conversation persists across navigation and full history is resent', async ({ page }) => {
    await login(page);
    const payloads: ChatPayload[] = [];
    await page.route('**/Chat/SendStream', async route => {
      payloads.push(route.request().postDataJSON() as ChatPayload);
      await route.fulfill(fulfillStream(replyEvents(`reply ${payloads.length}`)));
    });

    await openWidget(page);
    await sendWidgetMessage(page, 'first');
    await expect(page.locator('#chat-widget-panel .chat-message')).toHaveCount(2);

    // History is stored per user under the documented sessionStorage key.
    const userId = await page.locator('#chat-widget-panel').getAttribute('data-user-id');
    const stored = await page.evaluate(id => sessionStorage.getItem(`sm.chat.history.${id}`), userId);
    expect(stored).toBeTruthy();

    await page.goto('/Student');
    await openWidget(page);
    await expect(page.locator('#chat-widget-panel .chat-message')).toHaveCount(2);
    await expect(page.locator('#chat-widget-panel .chat-bubble-user')).toContainText('first');
    await expect(page.locator('#chat-widget-panel .chat-markdown')).toContainText('reply 1');

    await sendWidgetMessage(page, 'second');
    await expect(page.locator('#chat-widget-panel .chat-message')).toHaveCount(4);

    expect(payloads).toHaveLength(2);
    expect(payloads[1].messages.map(m => m.role)).toEqual(['user', 'assistant', 'user']);
    expect(payloads[1].messages.map(m => m.text)).toEqual(['first', 'reply 1', 'second']);
  });

  test('Conversation persists across reload on a widget page', async ({ page }) => {
    await login(page);
    await page.route('**/Chat/SendStream', route =>
      route.fulfill(fulfillStream(replyEvents('Survives reload.'))));

    await openWidget(page);
    await sendWidgetMessage(page, 'hello reload');
    await expect(page.locator('#chat-widget-panel .chat-message')).toHaveCount(2);

    await page.reload();

    await openWidget(page);
    await expect(page.locator('#chat-widget-panel .chat-message')).toHaveCount(2);
    await expect(page.locator('#chat-widget-panel .chat-bubble-user')).toContainText('hello reload');
    await expect(page.locator('#chat-widget-panel .chat-markdown')).toContainText('Survives reload.');
  });

  test('Open state persists across navigation and focuses the input', async ({ page }) => {
    await login(page);

    await page.locator('#chat-widget-toggle').click();
    await expect(page.locator('#chat-widget-panel')).toBeVisible();
    await expect(page.locator('#chat-input')).toBeFocused();
    expect(await page.evaluate(() => sessionStorage.getItem('sm.chat.widgetOpen'))).toBe('1');

    await page.goto('/Course');

    await expect(page.locator('#chat-widget-panel')).toBeVisible();
    expect(await page.evaluate(() => sessionStorage.getItem('sm.chat.widgetOpen'))).toBe('1');
  });

  test('Teacher sees the widget too', async ({ page }) => {
    await loginAs(page, 'teacher@teacher.com', 'Teacher123');

    await expect(page.locator('#chat-widget-toggle')).toBeVisible();
    await openWidget(page);
    await expect(page.locator('#chat-widget-panel')).toHaveAttribute('data-user-id', /.+/);
    await expect(page.locator('#chat-input')).toBeVisible();
  });

  test('Admin disabling chat hides the widget and re-enabling restores it', async ({ page }) => {
    await login(page);
    await page.goto('/Settings/Settings');
    await expect(page.getByRole('heading', { name: 'Chat Settings' })).toBeVisible();
    await expect(page.locator('#ChatEnabled')).toBeChecked();

    try {
      await page.locator('#ChatEnabled').setChecked(false);
      await page.getByRole('button', { name: 'Save Settings' }).click();
      await expect(page.locator('.alert-success')).toContainText('Settings saved.');

      await page.goto('/');
      await expect(page.locator('#chat-widget-toggle')).toHaveCount(0);
      await expect(page.locator('#chat-widget-panel')).toHaveCount(0);
    } finally {
      // Restore so other tests see the baseline configuration.
      await page.goto('/Settings/Settings');
      await page.locator('#ChatEnabled').setChecked(true);
      await page.getByRole('button', { name: 'Save Settings' }).click();
      await expect(page.locator('.alert-success')).toContainText('Settings saved.');
    }

    await page.goto('/');
    await expect(page.locator('#chat-widget-toggle')).toBeVisible();
  });

  test('Corrupt stored history is sanitized on the widget page and chat still works', async ({ page }) => {
    await login(page);
    const errors: string[] = [];
    page.on('pageerror', err => errors.push(err.message));

    // Seed the real per-user key with tampered entries, then reload to hit restore.
    const userId = await page.locator('#chat-widget-panel').getAttribute('data-user-id');
    const key = `sm.chat.history.${userId}`;
    const corrupt = {
      history: [
        { role: 'user', text: 'numeric name', fileName: 123, fileData: 'aGVsbG8=' },
        { role: 'user', text: 'xss', fileName: 'evil.png', fileData: 'x" onerror="window.__chatXss=1' },
        { role: 'system', text: 'invalid role' },
        { role: 'assistant', text: 42 },
        { role: 'user', text: 'real image', fileName: 'pixel.png', fileData: PixelPngBase64 },
        { role: 'assistant', text: 'kept reply' },
      ],
      meta: [null, null, null, null, null, { elapsedMs: 120, inputTokens: 10, outputTokens: 5 }],
    };
    await page.evaluate(({ k, v }) => sessionStorage.setItem(k, v), { k: key, v: JSON.stringify(corrupt) });

    await page.reload();
    await expect(page.locator('#chat-widget-toggle')).toBeVisible();

    await openWidget(page);
    const panel = page.locator('#chat-widget-panel');
    // Invalid entries dropped, invalid attachment fields stripped, valid image kept.
    await expect(panel.locator('.chat-message')).toHaveCount(4);
    // fileData stripped: plain text only, no attachment label or image for the tampered entry.
    await expect(panel.locator('.chat-bubble-user').nth(1)).toContainText('xss');
    await expect(panel.locator('.chat-bubble-user').nth(1)).not.toContainText('evil.png');
    await expect(panel.locator('.chat-image img')).toHaveCount(1);
    await expect(panel.locator('.chat-image img')).toHaveAttribute('src', `data:image/png;base64,${PixelPngBase64}`);
    await expect(panel.locator('[onerror]')).toHaveCount(0);
    expect(await page.evaluate(() => (window as unknown as { __chatXss?: number }).__chatXss)).toBeUndefined();
    expect(errors).toEqual([]);

    // The corrupt payload is cleaned out of storage.
    const storedRaw = await page.evaluate(k => sessionStorage.getItem(k), key);
    expect(storedRaw).toBeTruthy();
    expect(storedRaw).not.toContain('onerror');
    const stored = JSON.parse(storedRaw!) as { history: Array<Record<string, unknown>> };
    expect(stored.history).toHaveLength(4);
    expect(stored.history[0]).toEqual({ role: 'user', text: 'numeric name' });
    expect(stored.history[1]).toEqual({ role: 'user', text: 'xss', fileName: 'evil.png' });

    // Chat still works after the sanitized restore.
    const payloads: ChatPayload[] = [];
    await page.route('**/Chat/SendStream', async route => {
      payloads.push(route.request().postDataJSON() as ChatPayload);
      await route.fulfill(fulfillStream(replyEvents('Still working.')));
    });
    await sendWidgetMessage(page, 'after corruption');
    await expect(panel.locator('.chat-message')).toHaveCount(6);
    await expect(panel.locator('.chat-markdown').last()).toContainText('Still working.');
    expect(payloads[0].messages.map(m => m.text)).toEqual(['numeric name', 'xss', 'real image', 'kept reply', 'after corruption']);
    expect(payloads[0].messages[0].fileData).toBeUndefined();
    expect(payloads[0].messages[1].fileData).toBeUndefined();
  });

  test('Chat history is isolated per user across logins', async ({ page }) => {
    await login(page);
    await page.route('**/Chat/SendStream', route =>
      route.fulfill(fulfillStream(replyEvents('Isolated reply.'))));

    await openWidget(page);
    await sendWidgetMessage(page, 'admin private question');
    await expect(page.locator('#chat-widget-panel .chat-message')).toHaveCount(2);

    const adminId = await page.locator('#chat-widget-panel').getAttribute('data-user-id');
    const adminKey = `sm.chat.history.${adminId}`;
    expect(await page.evaluate(k => sessionStorage.getItem(k), adminKey)).toContain('admin private question');

    await logout(page);
    await loginAs(page, 'teacher@teacher.com', 'Teacher123');

    // Same tab, same sessionStorage — but a different per-user key and an empty transcript.
    const teacherId = await page.locator('#chat-widget-panel').getAttribute('data-user-id');
    expect(teacherId).toBeTruthy();
    expect(teacherId).not.toBe(adminId);
    const teacherKey = `sm.chat.history.${teacherId}`;
    expect(teacherKey).not.toBe(adminKey);

    await openWidget(page);
    await expect(page.locator('#chat-widget-panel .chat-message')).toHaveCount(0);
    expect(await page.evaluate(k => sessionStorage.getItem(k), teacherKey)).toBeNull();

    await sendWidgetMessage(page, 'teacher question');
    await expect(page.locator('#chat-widget-panel .chat-message')).toHaveCount(2);
    const teacherStored = await page.evaluate(k => sessionStorage.getItem(k), teacherKey);
    expect(teacherStored).toContain('teacher question');
    expect(teacherStored).not.toContain('admin private question');
    expect(await page.evaluate(k => sessionStorage.getItem(k), adminKey)).toContain('admin private question');
  });

  test('Quota fallback persists history without fileData when storage is full', async ({ page }) => {
    // Must be installed before login: addInitScript only runs on subsequent document loads.
    // Throw exactly once, on the first history write (the send-time persist).
    await page.addInitScript(() => {
      const original = Storage.prototype.setItem;
      let thrown = false;
      Storage.prototype.setItem = function (this: Storage, key: string, value: string) {
        if (!thrown && key.startsWith('sm.chat.history.')) {
          thrown = true;
          throw new Error('QuotaExceededError');
        }
        original.call(this, key, value);
      };
    });
    await login(page);

    // Hold the SSE response open so storage can be inspected right after the first
    // persist, before the post-stream persist rewrites the full history.
    let releaseStream!: () => void;
    const gate = new Promise<void>(resolve => { releaseStream = resolve; });
    await page.route('**/Chat/SendStream', async route => {
      await gate;
      await route.fulfill(fulfillStream(replyEvents('Slim persisted.')));
    });

    await openWidget(page);
    await page.locator('#chat-file-input').setInputFiles({
      name: 'notes.txt',
      mimeType: 'text/plain',
      buffer: Buffer.from('hello attachment', 'utf8'),
    });
    await expect(page.locator('#chat-file-preview .file-name')).toHaveText('notes.txt');
    await sendWidgetMessage(page, 'quota test');
    await expect(page.locator('#chat-widget-panel .chat-bubble-user')).toContainText('quota test [notes.txt]');

    // The first persist threw; the slim retry kept text + fileName but dropped fileData.
    const userId = await page.locator('#chat-widget-panel').getAttribute('data-user-id');
    const storedRaw = await page.evaluate(id => sessionStorage.getItem(`sm.chat.history.${id}`), userId);
    expect(storedRaw).toBeTruthy();
    const stored = JSON.parse(storedRaw!) as {
      history: Array<{ role: string; text: string; fileName?: string; fileData?: string }>;
    };
    expect(stored.history).toHaveLength(1);
    expect(stored.history[0]).toEqual({ role: 'user', text: 'quota test', fileName: 'notes.txt' });

    releaseStream();
    await expect(page.locator('#chat-widget-panel .chat-markdown')).toContainText('Slim persisted.');
    await expect(page.locator('#chat-send')).toBeEnabled();
  });

  test('SendStream rejects a tampered antiforgery token with 400', async ({ page }) => {
    await login(page);
    await openWidget(page);

    // No route mock: this hits the real server's antiforgery validation.
    const status = await page.evaluate(async () => {
      const response = await fetch('/Chat/SendStream', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'RequestVerificationToken': 'tampered-token',
        },
        body: JSON.stringify({ messages: [{ role: 'user', text: 'hi' }] }),
      });
      return response.status;
    });
    expect(status).toBe(400);
  });
});
