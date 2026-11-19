// Floating chat widget: open/close, Escape handling and open-state persistence.
// No-op on pages without the widget (anonymous, chat disabled, full Chat page).
$(document).ready(function () {
    const toggle = $('#chat-widget-toggle');
    if (toggle.length === 0) {
        return;
    }

    const panel = $('#chat-widget-panel');
    const storageKey = 'sm.chat.widgetOpen';

    function setOpen(open) {
        panel.toggleClass('d-none', !open);
        toggle.attr('aria-expanded', open ? 'true' : 'false');
        try {
            sessionStorage.setItem(storageKey, open ? '1' : '0');
        } catch (error) {
            // Open-state persistence is best-effort.
        }
        if (open) {
            $('#chat-input').focus();
        }
    }

    toggle.on('click', function () {
        setOpen(panel.hasClass('d-none'));
    });

    $('#chat-widget-close').on('click', function () {
        setOpen(false);
    });

    $(document).on('keydown', function (e) {
        if (e.key === 'Escape' && !panel.hasClass('d-none')) {
            setOpen(false);
        }
    });

    let initiallyOpen = false;
    try {
        initiallyOpen = sessionStorage.getItem(storageKey) === '1';
    } catch (error) {
        initiallyOpen = false;
    }
    if (initiallyOpen) {
        setOpen(true);
    }
});
