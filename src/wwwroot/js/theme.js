// Theme toggle — persists to localStorage, syncs to <html data-theme>
(function () {
    const STORAGE_KEY = 'theme';
    const LIGHT = 'corporate';
    const DARK = 'business';

    function getPreferred() {
        const saved = localStorage.getItem(STORAGE_KEY);
        if (saved) return saved;
        return window.matchMedia('(prefers-color-scheme: dark)').matches ? DARK : LIGHT;
    }

    function apply(theme) {
        document.documentElement.setAttribute('data-theme', theme);
        localStorage.setItem(STORAGE_KEY, theme);
        // Update toggle button state
        const toggles = document.querySelectorAll('.theme-toggle');
        toggles.forEach(t => t.checked = theme === DARK);
    }

    // Apply immediately to prevent flash
    apply(getPreferred());

    // Bind toggle handlers after DOM ready
    document.addEventListener('DOMContentLoaded', function () {
        document.querySelectorAll('.theme-toggle').forEach(function (toggle) {
            toggle.checked = getPreferred() === DARK;
            toggle.addEventListener('change', function () {
                apply(this.checked ? DARK : LIGHT);
            });
        });
    });

    // Listen for system preference changes
    window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', function (e) {
        if (!localStorage.getItem(STORAGE_KEY)) {
            apply(e.matches ? DARK : LIGHT);
        }
    });
})();
