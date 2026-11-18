let chatHistory = [];
let messageMeta = [];
let attachedFile = null;
let isStreaming = false;
let failedRequest = false;

$(document).ready(function () {
    if (typeof marked !== 'undefined') {
        marked.setOptions({
            breaks: true,
            gfm: true
        });
    }

    $('#chat-send').on('click', sendMessage);
    $('#chat-input').on('keypress', function (e) {
        if (e.which === 13) {
            sendMessage();
        }
    });

    $('#chat-file-input').on('change', function () {
        const file = this.files[0];
        if (!file) {
            clearFileAttachment();
            return;
        }

        const allowedExtensions = ['.txt', '.docx', '.pdf', '.xlsx', '.xls', '.png', '.jpg', '.jpeg', '.gif', '.webp'];
        const ext = '.' + file.name.split('.').pop().toLowerCase();
        if (!allowedExtensions.includes(ext)) {
            toastr.error('Unsupported file type. Allowed: ' + allowedExtensions.join(', '));
            $(this).val('');
            return;
        }

        const reader = new FileReader();
        reader.onload = function (e) {
            attachedFile = {
                name: file.name,
                data: e.target.result.split(',')[1]
            };
            showFilePreview(file.name);
        };
        reader.readAsDataURL(file);
    });

    $(document).on('click', '.chat-retry-btn', function () {
        if (isStreaming || !failedRequest) {
            return;
        }
        removeFailureNotice();
        streamReply();
    });

    $(document).on('click', '.chat-edit-btn', function () {
        if (isStreaming) {
            return;
        }
        startEdit(parseInt($(this).closest('.chat-message').attr('data-history-index'), 10));
    });

    $(document).on('click', '.chat-edit-save', function () {
        saveEdit(parseInt($(this).closest('.chat-message').attr('data-history-index'), 10));
    });

    $(document).on('click', '.chat-edit-cancel', function () {
        renderTranscript();
    });
});

function showFilePreview(fileName) {
    const preview = $('#chat-file-preview');
    preview.removeClass('d-none');
    preview.find('.file-name').text(fileName);
}

function clearFileAttachment() {
    attachedFile = null;
    const preview = $('#chat-file-preview');
    preview.addClass('d-none');
    preview.find('.file-name').text('');
    $('#chat-file-input').val('');
}

function sendMessage() {
    if (isStreaming) {
        return;
    }

    const input = $('#chat-input');
    const text = input.val().trim();

    if (!text && !attachedFile) {
        return;
    }

    removeFailureNotice();
    failedRequest = false;

    const message = { role: 'user', text: text };
    let displayText = text;
    let imageData = null;

    if (attachedFile) {
        const imageExts = ['.png', '.jpg', '.jpeg', '.gif', '.webp'];
        const ext = '.' + attachedFile.name.split('.').pop().toLowerCase();

        message.fileName = attachedFile.name;
        message.fileData = attachedFile.data;

        if (imageExts.includes(ext)) {
            imageData = attachedFile.data;
        } else {
            displayText = text ? text + ' [' + attachedFile.name + ']' : '[' + attachedFile.name + ']';
        }
    }

    chatHistory.push(message);
    messageMeta.push(null);
    appendMessage(chatHistory.length - 1, 'user', displayText, imageData);

    input.val('');
    clearFileAttachment();
    streamReply();
}

// Streams the reply from /Chat/SendStream (SSE over fetch). The current chatHistory
// is sent as-is, so retrying a failed request just calls this again.
async function streamReply() {
    isStreaming = true;
    setInputEnabled(false);
    setStatus('Working…');

    let assistantText = '';
    let assistantIndex = -1;
    let renderTimer = null;
    let done = null;

    // A failed request must not leave a partial assistant turn in the history:
    // retry re-sends the exact same messages, ending with the user's question.
    const discardPartialAssistant = function () {
        if (assistantIndex >= 0) {
            chatHistory.splice(assistantIndex, 1);
            messageMeta.splice(assistantIndex, 1);
            $(`.chat-message[data-history-index="${assistantIndex}"]`).remove();
            assistantIndex = -1;
        }
    };

    const renderNow = function () {
        if (assistantIndex >= 0) {
            updateAssistantBubble(assistantIndex, assistantText);
        }
    };
    const scheduleRender = function () {
        if (renderTimer) {
            return;
        }
        renderTimer = setTimeout(function () {
            renderTimer = null;
            renderNow();
        }, 80);
    };

    try {
        const response = await fetch('/Chat/SendStream', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': getAntiForgeryToken()
            },
            body: JSON.stringify({ messages: chatHistory })
        });

        if (!response.ok) {
            const data = await response.json().catch(() => ({}));
            toastr.error(data.error || 'Failed to get a response.');
            if (response.status === 429 && typeof data.used !== 'undefined') {
                updateQuota({ used: data.used, limit: data.limit, unlimited: false, remaining: 0 });
            }
            markFailed();
            return;
        }

        const reader = response.body.getReader();
        const decoder = new TextDecoder();
        let buffer = '';

        while (true) {
            const chunk = await reader.read();
            if (chunk.done) {
                break;
            }
            buffer += decoder.decode(chunk.value, { stream: true });

            let separator;
            while ((separator = buffer.indexOf('\n\n')) >= 0) {
                const frame = buffer.slice(0, separator);
                buffer = buffer.slice(separator + 2);

                const line = frame.split('\n').find(l => l.startsWith('data: '));
                if (!line) {
                    continue;
                }

                let event;
                try {
                    event = JSON.parse(line.slice(6));
                } catch (parseError) {
                    continue;
                }

                if (event.type === 'status') {
                    setStatus(event.status);
                } else if (event.type === 'delta') {
                    assistantText += event.text;
                    if (assistantIndex < 0) {
                        chatHistory.push({ role: 'assistant', text: '' });
                        messageMeta.push(null);
                        assistantIndex = chatHistory.length - 1;
                        appendMessage(assistantIndex, 'assistant', '');
                    }
                    scheduleRender();
                } else if (event.type === 'done') {
                    done = event;
                } else if (event.type === 'error') {
                    toastr.error(event.error || 'The chat service is currently unavailable.');
                    discardPartialAssistant();
                    markFailed();
                    return;
                }
            }
        }

        if (renderTimer) {
            clearTimeout(renderTimer);
            renderTimer = null;
        }

        if (assistantIndex >= 0) {
            chatHistory[assistantIndex].text = assistantText;
            messageMeta[assistantIndex] = done
                ? { elapsedMs: done.elapsedMs, inputTokens: done.inputTokens, outputTokens: done.outputTokens }
                : null;
            updateAssistantBubble(assistantIndex, assistantText, messageMeta[assistantIndex]);
        }

        if (done && done.quota) {
            updateQuota(done.quota);
        }

        if (assistantIndex < 0) {
            // Stream ended without any text: treat as a failure so the user can retry.
            markFailed();
        }
    } catch (error) {
        toastr.error('The chat service is currently unavailable. Please ensure your local AI server is running and configured correctly.');
        discardPartialAssistant();
        markFailed();
    } finally {
        if (renderTimer) {
            clearTimeout(renderTimer);
        }
        isStreaming = false;
        setInputEnabled(true);
        $('#chat-input').focus();
    }
}

function markFailed() {
    failedRequest = true;
    removeFailureNotice();
    const notice = $(`
        <div class="chat-failure-notice alert alert-warning py-2 d-flex justify-content-between align-items-center">
            <span class="small">The reply failed. Your message was kept.</span>
            <button type="button" class="btn btn-sm btn-outline-primary chat-retry-btn">Retry</button>
        </div>`);
    $('#chat-messages').append(notice);
    scrollToEnd();
}

function removeFailureNotice() {
    $('.chat-failure-notice').remove();
}

function startEdit(index) {
    const message = chatHistory[index];
    if (!message || message.role !== 'user') {
        return;
    }

    const bubble = $(`.chat-message[data-history-index="${index}"]`);
    const displayText = message.text || '';
    const editor = $(`
        <div class="chat-editor">
            <textarea class="form-control form-control-sm mb-2" rows="2"></textarea>
            <button type="button" class="btn btn-sm btn-primary chat-edit-save">Save &amp; resend</button>
            <button type="button" class="btn btn-sm btn-outline-secondary chat-edit-cancel ms-1">Cancel</button>
        </div>`);
    editor.find('textarea').val(displayText);
    bubble.find('.chat-bubble').empty().append(editor);
    editor.find('textarea').focus();
}

function saveEdit(index) {
    const bubble = $(`.chat-message[data-history-index="${index}"]`);
    const newText = bubble.find('textarea').val().trim();
    if (!newText) {
        toastr.error('Message text is required.');
        return;
    }

    if (index < chatHistory.length - 1 &&
        !confirm('Editing this message will discard the later messages. Continue?')) {
        renderTranscript();
        return;
    }

    // Truncate everything after the edited message, then resend.
    chatHistory = chatHistory.slice(0, index + 1);
    messageMeta = messageMeta.slice(0, index + 1);
    chatHistory[index].text = newText;

    removeFailureNotice();
    failedRequest = false;
    renderTranscript();
    streamReply();
}

function renderTranscript() {
    const messages = $('#chat-messages');
    messages.empty();
    for (let i = 0; i < chatHistory.length; i++) {
        const message = chatHistory[i];
        if (message.role === 'user') {
            let displayText = message.text || '';
            let imageData = null;
            if (message.fileName && message.fileData) {
                const imageExts = ['.png', '.jpg', '.jpeg', '.gif', '.webp'];
                const ext = '.' + message.fileName.split('.').pop().toLowerCase();
                if (imageExts.includes(ext)) {
                    imageData = message.fileData;
                } else {
                    displayText = displayText ? displayText + ' [' + message.fileName + ']' : '[' + message.fileName + ']';
                }
            }
            appendMessage(i, 'user', displayText, imageData);
        } else {
            appendMessage(i, 'assistant', message.text || '', null, messageMeta[i]);
        }
    }
    scrollToEnd();
}

function appendMessage(index, role, text, imageData, meta) {
    const messages = $('#chat-messages');
    const isUser = role === 'user';
    const alignment = isUser ? 'text-end' : 'text-start';
    const label = isUser ? 'You' : 'Assistant';
    const bubbleClass = isUser
        ? 'chat-bubble chat-bubble-user bg-primary text-white'
        : 'chat-bubble chat-bubble-assistant bg-white border';

    let imageHtml = '';
    if (imageData) {
        imageHtml = `<div class="chat-image mb-2"><img src="data:image/png;base64,${imageData}" class="img-fluid rounded" style="max-height: 300px;" /></div>`;
    }

    const bodyContent = isUser
        ? `<div class="chat-body">${imageHtml}${escapeHtml(text)}</div>`
        : `<div class="chat-markdown">${renderMarkdown(text)}</div>`;

    const editButton = isUser
        ? '<button type="button" class="btn btn-link btn-sm p-0 ms-2 text-muted chat-edit-btn" title="Edit message">&#9998;</button>'
        : '';

    const messageHtml = `
        <div class="chat-message mb-3 ${alignment}" data-history-index="${index}">
            <small class="text-muted d-block mb-1">${label}${editButton}</small>
            <div class="${bubbleClass} rounded p-3">
                ${bodyContent}${metaHtml(meta)}
            </div>
        </div>`;

    messages.append(messageHtml);
    scrollToEnd();
}

function updateAssistantBubble(index, text, meta) {
    const bubble = $(`.chat-message[data-history-index="${index}"] .chat-bubble`);
    bubble.find('.chat-markdown').html(renderMarkdown(text));
    bubble.find('.chat-meta').remove();
    if (meta) {
        bubble.append(metaHtml(meta));
    }
    scrollToEnd();
}

function metaHtml(meta) {
    if (!meta || typeof meta.elapsedMs !== 'number') {
        return '';
    }
    const seconds = (Number(meta.elapsedMs) / 1000).toFixed(1);
    const tokens = (Number(meta.inputTokens) || 0) + (Number(meta.outputTokens) || 0);
    return `<div class="chat-meta small text-muted mt-2">${seconds}s &middot; ${tokens} tokens</div>`;
}

function setStatus(text) {
    $('#chat-status-text').text(text);
}

function scrollToEnd() {
    const messages = $('#chat-messages');
    messages.scrollTop(messages.prop('scrollHeight'));
}

function renderMarkdown(text) {
    if (typeof marked === 'undefined') {
        return escapeHtml(text);
    }

    const normalized = normalizeAssistantMarkdown(text);
    const html = marked.parse(normalized);

    // Model output is untrusted: never inject raw HTML into the page.
    if (typeof DOMPurify !== 'undefined') {
        return DOMPurify.sanitize(html);
    }

    // If the sanitizer is unavailable (offline/CDN failure), fail closed.
    return escapeHtml(text);
}

function normalizeAssistantMarkdown(text) {
    return text
        .replace(/:\s+(\*\*\d+\.)/g, ':\n\n$1')
        .replace(/([.!?])\s+(\*\*\d+\.)/g, '$1\n\n$2')
        .replace(/(\*\*[^*]+:\*\*)\s+(\*)/g, '$1\n$2')
        .replace(/:\s+(\*\s+\*\*)/g, ':\n$1')
        .replace(/(\S)\s+(\*\s+\*\*)/g, '$1\n$2');
}

function getAntiForgeryToken() {
    return $('input[name="__RequestVerificationToken"]').val() || '';
}

function setInputEnabled(enabled) {
    $('#chat-input').prop('disabled', !enabled);
    $('#chat-send').prop('disabled', !enabled);
    $('#chat-file-input').prop('disabled', !enabled);
    $('#chat-loading').toggleClass('d-none', enabled);
}

function updateQuota(quota) {
    if (!quota || typeof quota.used !== 'number') {
        return;
    }

    const indicator = $('#chat-quota');
    indicator.attr('data-used', quota.used);
    $('#chat-quota-used').text(quota.used);

    if (!quota.unlimited && quota.remaining <= 0) {
        indicator.addClass('text-danger').removeClass('text-muted');
        toastr.warning('Daily token limit reached. Your quota resets at midnight UTC.');
    }
}

function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}