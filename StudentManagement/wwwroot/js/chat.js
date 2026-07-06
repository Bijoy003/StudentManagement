let chatHistory = [];

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
});

async function sendMessage() {
    const input = $('#chat-input');
    const text = input.val().trim();

    if (!text) {
        return;
    }

    appendMessage('user', text);
    chatHistory.push({ role: 'user', text: text });

    input.val('');
    setInputEnabled(false);

    try {
        const response = await fetch('/Chat/Send', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ messages: chatHistory })
        });

        const data = await response.json();

        if (!response.ok) {
            toastr.error(data.error || 'Failed to get a response.');
            chatHistory.pop();
            return;
        }

        appendMessage('assistant', data.reply);
        chatHistory.push({ role: 'assistant', text: data.reply });
    } catch (error) {
        toastr.error('The chat service is currently unavailable. Please ensure LM Studio is running with the local server enabled.');
        chatHistory.pop();
    } finally {
        setInputEnabled(true);
        input.focus();
    }
}

function appendMessage(role, text) {
    const messages = $('#chat-messages');
    const isUser = role === 'user';
    const alignment = isUser ? 'text-end' : 'text-start';
    const label = isUser ? 'You' : 'Assistant';
    const bubbleClass = isUser
        ? 'chat-bubble chat-bubble-user bg-primary text-white'
        : 'chat-bubble chat-bubble-assistant bg-white border';
    const bodyContent = isUser
        ? `<div class="chat-body">${escapeHtml(text)}</div>`
        : `<div class="chat-markdown">${renderMarkdown(text)}</div>`;

    const messageHtml = `
        <div class="chat-message mb-3 ${alignment}">
            <small class="text-muted d-block mb-1">${label}</small>
            <div class="${bubbleClass} rounded p-3">
                ${bodyContent}
            </div>
        </div>`;

    messages.append(messageHtml);
    messages.scrollTop(messages.prop('scrollHeight'));
}

function renderMarkdown(text) {
    if (typeof marked === 'undefined') {
        return escapeHtml(text);
    }

    const normalized = normalizeAssistantMarkdown(text);
    return marked.parse(normalized);
}

function normalizeAssistantMarkdown(text) {
    return text
        .replace(/:\s+(\*\*\d+\.)/g, ':\n\n$1')
        .replace(/([.!?])\s+(\*\*\d+\.)/g, '$1\n\n$2')
        .replace(/(\*\*[^*]+:\*\*)\s+(\*)/g, '$1\n$2')
        .replace(/:\s+(\*\s+\*\*)/g, ':\n$1')
        .replace(/(\S)\s+(\*\s+\*\*)/g, '$1\n$2');
}

function setInputEnabled(enabled) {
    $('#chat-input').prop('disabled', !enabled);
    $('#chat-send').prop('disabled', !enabled);
    $('#chat-loading').toggleClass('d-none', enabled);
}

function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}
