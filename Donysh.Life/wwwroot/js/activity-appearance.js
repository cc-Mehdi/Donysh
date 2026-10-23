document.querySelectorAll('[data-activity-appearance]').forEach((picker) => {
  const form = picker.closest('form');
  const preview = picker.querySelector('[data-appearance-preview]');
  const previewTitle = picker.querySelector('[data-preview-title]');
  const previewIcon = preview?.querySelector('use');
  if (!form || !preview || !previewTitle || !previewIcon) return;

  const update = () => {
    const title = form.querySelector('[name="Input.Title"]')?.value.trim();
    const colorClass = picker.querySelector('[name="Input.ColorHex"]:checked')?.dataset.colorClass;
    const icon = picker.querySelector('[name="Input.IconKey"]:checked');
    const source = icon?.closest('.icon-option')?.querySelector('use');
    previewTitle.textContent = title || 'فعالیت من';
    if (colorClass && colorClass !== preview.dataset.colorClass) {
      preview.classList.remove(preview.dataset.colorClass);
      preview.classList.add(colorClass);
      preview.dataset.colorClass = colorClass;
    }
    if (source) previewIcon.setAttribute('href', source.getAttribute('href'));
  };

  form.querySelector('[name="Input.Title"]')?.addEventListener('input', update);
  picker.addEventListener('change', update);
  update();
});
