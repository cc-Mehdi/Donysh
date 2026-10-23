(() => {
    const key = 'donysh-life-theme';
    const prefersDark = window.matchMedia('(prefers-color-scheme: dark)');

    function preference() {
        try {
            const value = localStorage.getItem(key);
            return value === 'dark' || value === 'light' ? value : null;
        }
        catch { return null; }
    }

    function apply(theme) {
        document.documentElement.dataset.lifeTheme = theme;
        document.querySelector('meta[name="theme-color"]')?.setAttribute('content', theme === 'dark' ? '#0d1726' : '#0d9488');
        document.querySelectorAll('[data-life-theme-toggle]').forEach(button => {
            const dark = theme === 'dark';
            const label = dark ? 'فعال کردن حالت روشن' : 'فعال کردن حالت تیره';
            button.setAttribute('aria-label', label);
            button.setAttribute('aria-pressed', String(dark));
            button.title = label;
            const text = button.querySelector('.theme-toggle-label');
            if (text) text.textContent = dark ? 'حالت روشن' : 'حالت تیره';
        });
    }

    function currentTheme() {
        const saved = preference();
        return saved === 'dark' || saved === 'light' ? saved : prefersDark.matches ? 'dark' : 'light';
    }

    apply(currentTheme());
    const onSystemChange = () => {
        if (!preference()) apply(currentTheme());
    };
    if (typeof prefersDark.addEventListener === 'function') prefersDark.addEventListener('change', onSystemChange);
    else prefersDark.addListener?.(onSystemChange);
    document.addEventListener('DOMContentLoaded', () => {
        apply(currentTheme());
        document.querySelectorAll('[data-life-theme-toggle]').forEach(button => button.addEventListener('click', () => {
            const next = document.documentElement.dataset.lifeTheme === 'dark' ? 'light' : 'dark';
            try { localStorage.setItem(key, next); } catch { /* Current page still changes. */ }
            apply(next);
        }));
    });
})();
