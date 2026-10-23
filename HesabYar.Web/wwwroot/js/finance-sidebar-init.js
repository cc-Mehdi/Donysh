try {
    if (localStorage.getItem('donysh-finance-sidebar') === 'collapsed') {
        document.documentElement.dataset.financeSidebar = 'collapsed';
    }
} catch {
    // The sidebar still works when browser storage is unavailable.
}
