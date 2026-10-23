(() => {
    const compact = window.matchMedia('(min-width: 901px) and (max-width: 1200px)');
    const storageKey = () => compact.matches ? 'donysh-life-sidebar-compact' : 'donysh-life-sidebar-wide';
    const toggle = () => document.querySelector('[data-life-sidebar-toggle]');

    function savedState() {
        try { return localStorage.getItem(storageKey()); }
        catch { return null; }
    }

    function setState(state) {
        document.documentElement.dataset.lifeSidebar = state;
        const button = toggle();
        if (!button) return;
        const collapsed = state === 'collapsed';
        const label = collapsed ? 'بازکردن سایدبار' : 'جمع‌کردن سایدبار';
        button.setAttribute('aria-expanded', String(!collapsed));
        button.setAttribute('aria-label', label);
        button.title = label;
    }

    function syncWithViewport() {
        const stored = savedState();
        setState(stored === 'collapsed' || stored === 'expanded'
            ? stored
            : compact.matches ? 'collapsed' : 'expanded');
    }

    syncWithViewport();
    compact.addEventListener('change', syncWithViewport);
    document.addEventListener('DOMContentLoaded', () => {
        syncWithViewport();
        toggle()?.addEventListener('click', () => {
            const state = document.documentElement.dataset.lifeSidebar === 'collapsed' ? 'expanded' : 'collapsed';
            try { localStorage.setItem(storageKey(), state); } catch { /* The choice still applies for this page. */ }
            setState(state);
        });
    });
})();
