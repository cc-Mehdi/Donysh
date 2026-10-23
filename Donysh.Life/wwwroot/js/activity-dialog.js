(() => {
    const dialog = document.querySelector('#activity-create-dialog');
    if (!dialog) return;

    const open = () => {
        if (!dialog.open) dialog.showModal();
        dialog.querySelector('[name="Input.Title"]')?.focus();
    };

    document.querySelectorAll('[data-open-activity-dialog]').forEach(button => {
        button.addEventListener('click', open);
    });
    dialog.querySelector('[data-close-activity-dialog]')?.addEventListener('click', () => dialog.close());
    dialog.addEventListener('click', event => {
        const bounds = dialog.getBoundingClientRect();
        if (event.target === dialog && (event.clientX < bounds.left || event.clientX > bounds.right
            || event.clientY < bounds.top || event.clientY > bounds.bottom)) dialog.close();
    });
    if (dialog.dataset.reopen === 'true') open();
})();
