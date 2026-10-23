try {
    if (localStorage.getItem('donysh-finance-theme') === 'dark') {
        document.documentElement.dataset.financeTheme = 'dark';
    }
} catch {
    // Storage may be unavailable; the dashboard remains usable in light mode.
}
