// Close modal on Escape key
document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape') {
        // Close DaisyUI modal
        var modal = document.querySelector('.modal.modal-open');
        if (modal) {
            modal.classList.remove('modal-open');
        }
    }
});

// ── Helper: reset all loading buttons ────────────────────────────────────
function resetLoadingButtons() {
    document.querySelectorAll('button.loading, [type="submit"].loading').forEach(function (btn) {
        btn.disabled = btn.dataset.htmxOrigDisabled === 'true';
        btn.classList.remove('loading', 'loading-spinner');
        delete btn.dataset.htmxOrigDisabled;
    });
}

// ── Clean up when any modal is dismissed via JS (Cancel / ✕ / backdrop) ──
(function () {
    var observer = new MutationObserver(function (mutations) {
        mutations.forEach(function (m) {
            if (m.type === 'attributes' && m.attributeName === 'class') {
                var target = m.target;
                if (target.classList.contains('modal') && !target.classList.contains('modal-open')) {
                    resetLoadingButtons();
                }
            }
        });
    });

    // Observe modal class changes on the whole document
    observer.observe(document.body, { attributes: true, subtree: true, attributeFilter: ['class'] });

    // Also catch when modal container is emptied (e.g. _ModalClose partial replaces content)
    var modalContainer = document.getElementById('modal-container');
    if (modalContainer) {
        var childObserver = new MutationObserver(function () {
            // If modal content was cleared, reset any stale loading buttons
            if (!document.querySelector('.modal.modal-open')) {
                resetLoadingButtons();
            }
        });
        childObserver.observe(modalContainer, { childList: true });
    }
})();

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

    // Catch-all: also clean up on htmx:afterRequest which fires for every
    // completed request regardless of swap outcome, handling edge cases where
    // afterSettle may not fire (e.g. modal dismissed mid-request).
    document.addEventListener('htmx:afterRequest', function (evt) {
        var trigger = evt.detail.elt;
        if (trigger && (trigger.tagName === 'BUTTON' || trigger.type === 'submit')) {
            trigger.disabled = trigger.dataset.htmxOrigDisabled === 'true';
            trigger.classList.remove('loading', 'loading-spinner');
            delete trigger.dataset.htmxOrigDisabled;
        }
    });
})();

// ── Auto-dismiss toast alerts ────────────────────────────────────────────
(function () {
    var toastContainer = document.getElementById('toast-container');
    if (!toastContainer) return;

    var observer = new MutationObserver(function (mutations) {
        mutations.forEach(function (m) {
            m.addedNodes.forEach(function (node) {
                if (node.nodeType === Node.ELEMENT_NODE && node.classList.contains('alert')) {
                    // Auto-dismiss after 5 seconds
                    setTimeout(function () {
                        node.style.transition = 'opacity 0.3s ease-out';
                        node.style.opacity = '0';
                        setTimeout(function () { node.remove(); }, 300);
                    }, 5000);
                }
            });
        });
    });

    observer.observe(toastContainer, { childList: true });
})();
