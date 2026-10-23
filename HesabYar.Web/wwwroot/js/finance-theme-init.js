(() => {
    const root = document.documentElement;
    const overrideKey = 'donysh-finance-theme-override';
    const sloganKey = 'donysh-finance-last-slogan';
    const slogans = [
        'خرج‌ها را که ببینی، تصمیم‌ها روشن‌تر می‌شوند.',
        'قدم‌های کوچک، آرامش مالی می‌سازند.',
        'از خرج‌های امروز، برای فردا راه پیدا کن.',
        'هر خرج ثبت‌شده، تصویری روشن‌تر از ماهت می‌دهد.',
        'برای خواسته‌هایت جا باز کن؛ از همین امروز.',
        'حساب روشن، خیال آسوده‌تر.'
    ];
    let memoryOverride = null;

    const nextThemeChange = (now) => {
        const next = new Date(now);
        if (now.getHours() < 6) next.setHours(6, 0, 0, 0);
        else if (now.getHours() < 18) next.setHours(18, 0, 0, 0);
        else {
            next.setDate(next.getDate() + 1);
            next.setHours(6, 0, 0, 0);
        }
        return next.getTime();
    };

    const readOverride = (now) => {
        let saved = memoryOverride;
        try {
            saved ??= JSON.parse(localStorage.getItem(overrideKey) || 'null');
        } catch {
            // The automatic schedule works without browser storage.
        }
        if (saved && (saved.theme === 'light' || saved.theme === 'dark') && saved.expiresAt > now.getTime()) {
            return saved.theme;
        }
        memoryOverride = null;
        try { localStorage.removeItem(overrideKey); } catch { /* Storage may be unavailable. */ }
        return null;
    };

    const greetingFor = (hour) => hour >= 6 && hour < 16
        ? 'روز بخیر'
        : hour >= 16 && hour < 20
            ? 'عصر بخیر'
            : 'شب بخیر';

    const refresh = () => {
        const now = new Date();
        const automatic = now.getHours() >= 6 && now.getHours() < 18 ? 'light' : 'dark';
        const theme = readOverride(now) || automatic;
        root.dataset.financeTheme = theme;
        document.querySelectorAll('[data-finance-theme-toggle]').forEach((button) => {
            button.setAttribute('aria-pressed', String(theme === 'dark'));
        });
        document.querySelectorAll('[data-dashboard-greeting]').forEach((element) => {
            element.textContent = greetingFor(now.getHours());
        });
    };

    const toggle = () => {
        const now = new Date();
        const theme = root.dataset.financeTheme === 'dark' ? 'light' : 'dark';
        memoryOverride = { theme, expiresAt: nextThemeChange(now) };
        try { localStorage.setItem(overrideKey, JSON.stringify(memoryOverride)); } catch { /* This visit still keeps the choice. */ }
        refresh();
    };

    const chooseSlogan = () => {
        const element = document.querySelector('[data-dashboard-slogan]');
        if (!element) return;
        let last = -1;
        try {
            const stored = localStorage.getItem(sloganKey);
            if (stored !== null) last = Number(stored);
        } catch { /* A fresh choice is enough. */ }
        const choices = slogans.map((_, index) => index).filter((index) => index !== last);
        const index = choices[Math.floor(Math.random() * choices.length)];
        element.textContent = slogans[index];
        try { localStorage.setItem(sloganKey, String(index)); } catch { /* No persistence needed. */ }
    };

    window.donyshFinanceTheme = { refresh, toggle };
    refresh();
    document.addEventListener('DOMContentLoaded', () => {
        chooseSlogan();
        refresh();
    });
    document.addEventListener('visibilitychange', () => {
        if (!document.hidden) refresh();
    });
    window.setInterval(refresh, 60_000);
})();
