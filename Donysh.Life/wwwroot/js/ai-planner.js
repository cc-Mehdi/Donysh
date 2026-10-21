(() => {
  const button = document.querySelector('[data-copy-ai-prompt]');
  if (!button) return;
  button.addEventListener('click', async () => {
    const status = document.querySelector('[data-copy-status]');
    try {
      await navigator.clipboard.writeText(document.querySelector('#ai-prompt').textContent);
      status.textContent = 'راهنما کپی شد.';
    } catch {
      status.textContent = 'کپی خودکار ممکن نشد؛ متن بالا را انتخاب و کپی کن.';
    }
  });
})();
