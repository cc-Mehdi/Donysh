(() => {
  const modes = document.querySelectorAll('[data-ai-mode]');
  const panes = document.querySelectorAll('[data-ai-mode-panel]');
  const modeField = document.querySelector('[data-ai-mode-value]');
  const selectMode = (mode) => {
    panes.forEach((pane) => { pane.hidden = pane.dataset.aiModePanel !== mode; });
    if (modeField) modeField.value = mode;
  };
  modes.forEach((radio) => radio.addEventListener('change', () => {
    if (radio.checked) selectMode(radio.value);
  }));
  selectMode(document.querySelector('[data-ai-mode]:checked')?.value || 'new');

  document.querySelectorAll('[data-copy-ai-prompt]').forEach((button) => {
    button.addEventListener('click', async () => {
      const target = document.getElementById(button.dataset.copyTarget);
      const status = button.parentElement.querySelector('[data-copy-status]');
      if (!target || !status) return;
      try {
        await navigator.clipboard.writeText(target.textContent);
        status.textContent = 'راهنما کپی شد.';
      } catch {
        status.textContent = 'کپی خودکار ممکن نشد؛ متن بالا را انتخاب و کپی کن.';
      }
    });
  });
})();
