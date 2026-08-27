let chatHistory = [];
let attachedFile = null;

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

async function sendMessage() {
    const input = $('#chat-input');
    const text = input.val().trim();

    if (!text && !attachedFile) {
        return;
    }

    var message = { role: 'user', text: text };
    var displayText = text;
    var imageData = null;

    if (attachedFile) {
        var imageExts = ['.png', '.jpg', '.jpeg', '.gif', '.webp'];
        var ext = '.' + attachedFile.name.split('.').pop().toLowerCase();

        message.fileName = attachedFile.name;
        message.fileData = attachedFile.data;

        if (imageExts.includes(ext)) {
            imageData = attachedFile.data;
        } else {
            displayText = text ? text + ' [' + attachedFile.name + ']' : '[' + attachedFile.name + ']';
        }
    }

    appendMessage('user', displayText, imageData);
    chatHistory.push(message);

    input.val('');
    clearFileAttachment();
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
        toastr.error('The chat service is currently unavailable. Please ensure your local AI server is running and configured correctly.');
        chatHistory.pop();
    } finally {
        setInputEnabled(true);
        input.focus();
    }
}

function appendMessage(role, text, imageData) {
    const messages = $('#chat-messages');
    const isUser = role === 'user';
    const alignment = isUser ? 'text-end' : 'text-start';
    const label = isUser ? 'You' : 'Assistant';
    const bubbleClass = isUser
        ? 'chat-bubble chat-bubble-user bg-primary text-white'
        : 'chat-bubble chat-bubble-assistant bg-white border';

    var imageHtml = '';
    if (imageData) {
        imageHtml = `<div class="chat-image mb-2"><img src="data:image/png;base64,${imageData}" class="img-fluid rounded" style="max-height: 300px;" /></div>`;
    }

    const bodyContent = isUser
        ? `<div class="chat-body">${imageHtml}${escapeHtml(text)}</div>`
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
    $('#chat-file-input').prop('disabled', !enabled);
    $('#chat-loading').toggleClass('d-none', enabled);
}

function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}