// Close modal on Escape key
document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape') {
        // Close DaisyUI modal
        const modal = document.querySelector('.modal.modal-open');
        if (modal) {
            modal.classList.remove('modal-open');
        }
    }
});

// ── HTMX global loading indicators & button disable ──────────────────────

// Track active requests for top progress bar
(function () {
    var activeRequests = 0;

    // Create a top progress bar element (hidden by default)
    var bar = document.createElement('div');
    bar.id = 'htmx-progress';
    bar.style.cssText =
        'position:fixed;top:0;left:0;height:3px;background:oklch(var(--p));' +
        'z-index:9999;transition:width .3s ease;width:0;opacity:0;pointer-events:none;';
    document.documentElement.appendChild(bar);

    function showBar() {
        bar.style.opacity = '1';
        bar.style.width = '70%';
    }

    function hideBar() {
        bar.style.width = '100%';
        setTimeout(function () {
            bar.style.opacity = '0';
            setTimeout(function () { bar.style.width = '0'; }, 300);
        }, 150);
    }

    document.addEventListener('htmx:beforeRequest', function (evt) {
        activeRequests++;
        // Disable the triggering element if it's a button or submit
        var trigger = evt.detail.elt;
        if (trigger && (trigger.tagName === 'BUTTON' || trigger.type === 'submit')) {
            trigger.dataset.htmxOrigDisabled = trigger.disabled;
            trigger.disabled = true;
            trigger.classList.add('loading', 'loading-spinner');
        }
        showBar();
    });

    document.addEventListener('htmx:afterSettle', function (evt) {
        activeRequests = Math.max(0, activeRequests - 1);
        // Re-enable the triggering element
        var trigger = evt.detail.elt;
        if (trigger && (trigger.tagName === 'BUTTON' || trigger.type === 'submit')) {
            trigger.disabled = trigger.dataset.htmxOrigDisabled === 'true';
            trigger.classList.remove('loading', 'loading-spinner');
            delete trigger.dataset.htmxOrigDisabled;
        }
        if (activeRequests === 0) hideBar();
    });

    document.addEventListener('htmx:responseError', function (evt) {
        activeRequests = Math.max(0, activeRequests - 1);
        var trigger = evt.detail.elt;
        if (trigger && (trigger.tagName === 'BUTTON' || trigger.type === 'submit')) {
            trigger.disabled = trigger.dataset.htmxOrigDisabled === 'true';
            trigger.classList.remove('loading', 'loading-spinner');
            delete trigger.dataset.htmxOrigDisabled;
        }
        if (activeRequests === 0) hideBar();
    });

    document.addEventListener('htmx:sendError', function (evt) {
        activeRequests = Math.max(0, activeRequests - 1);
        var trigger = evt.detail.elt;
        if (trigger && (trigger.tagName === 'BUTTON' || trigger.type === 'submit')) {
            trigger.disabled = trigger.dataset.htmxOrigDisabled === 'true';
            trigger.classList.remove('loading', 'loading-spinner');
            delete trigger.dataset.htmxOrigDisabled;
        }
        if (activeRequests === 0) hideBar();
    });
})();
