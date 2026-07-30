(() => {
  const toLatinDigits = (value) => String(value ?? '')
    .replace(/[۰-۹]/g, (digit) => String('۰۱۲۳۴۵۶۷۸۹'.indexOf(digit)))
    .replace(/[٠-٩]/g, (digit) => String('٠١٢٣٤٥٦٧٨٩'.indexOf(digit)));

  const formatMoney = (value) => {
    const digits = toLatinDigits(value).replace(/[^0-9]/g, '').replace(/^0+(?=\d)/, '');
    if (!digits) return '';
    return digits.replace(/\B(?=(\d{3})+(?!\d))/g, ',');
  };

  document.querySelectorAll('[data-money]').forEach((input) => {
    input.value = formatMoney(input.value);
    input.addEventListener('input', () => {
      const cursorAtEnd = input.selectionStart === input.value.length;
      input.value = formatMoney(input.value);
      if (cursorAtEnd) input.setSelectionRange(input.value.length, input.value.length);
    });
    input.addEventListener('blur', () => {
      if (input.dataset.allowZero === 'true' && input.value === '') input.value = '0';
    });
  });

  const formatPersianDateInput = (value) => {
    const digits = toLatinDigits(value).replace(/[^0-9]/g, '').slice(0, 8);
    if (digits.length <= 4) return digits;
    if (digits.length <= 6) return `${digits.slice(0, 4)}/${digits.slice(4)}`;
    return `${digits.slice(0, 4)}/${digits.slice(4, 6)}/${digits.slice(6)}`;
  };

  document.querySelectorAll('[data-persian-date]').forEach((input) => {
    input.value = formatPersianDateInput(input.value);
    input.addEventListener('input', () => {
      input.value = formatPersianDateInput(input.value);
      input.setSelectionRange(input.value.length, input.value.length);
    });
  });

  const sidebar = document.getElementById('sidebar');
  document.querySelectorAll('[data-open-sidebar]').forEach((button) => {
    button.addEventListener('click', () => sidebar?.classList.remove('hidden'));
  });
  document.querySelectorAll('[data-close-sidebar]').forEach((button) => {
    button.addEventListener('click', () => sidebar?.classList.add('hidden'));
  });

  document.querySelectorAll('[data-confirm]').forEach((element) => {
    element.addEventListener('click', (event) => {
      const message = element.getAttribute('data-confirm') || 'آیا مطمئن هستید؟';
      if (!window.confirm(message)) event.preventDefault();
    });
  });

  const guide = document.getElementById('onboarding-guide');
  if (guide) {
    const user = guide.dataset.onboardingUser || 'anonymous';
    const storageKey = `hesabyar:onboarding:v2:${user}`;
    const steps = [...guide.querySelectorAll('[data-guide-step]')];
    const dots = [...guide.querySelectorAll('[data-guide-dot]')];
    const previousButton = guide.querySelector('[data-guide-prev]');
    const nextButton = guide.querySelector('[data-guide-next]');
    let currentStep = 0;

    const renderGuide = () => {
      steps.forEach((step, index) => step.classList.toggle('hidden', index !== currentStep));
      dots.forEach((dot, index) => dot.classList.toggle('guide-dot-active', index === currentStep));
      previousButton?.classList.toggle('hidden', currentStep === 0);
      if (nextButton) nextButton.textContent = currentStep === steps.length - 1 ? 'شروع استفاده' : 'مرحله بعد';
    };

    const openGuide = () => {
      currentStep = 0;
      renderGuide();
      guide.classList.remove('hidden');
      document.body.classList.add('overflow-hidden');
    };

    const closeGuide = (remember = true) => {
      guide.classList.add('hidden');
      document.body.classList.remove('overflow-hidden');
      if (remember) localStorage.setItem(storageKey, 'done');
    };

    document.querySelectorAll('[data-guide-open]').forEach((button) => button.addEventListener('click', openGuide));
    guide.querySelectorAll('[data-guide-close], [data-guide-skip]').forEach((button) => button.addEventListener('click', () => closeGuide(true)));
    previousButton?.addEventListener('click', () => {
      currentStep = Math.max(0, currentStep - 1);
      renderGuide();
    });
    nextButton?.addEventListener('click', () => {
      if (currentStep >= steps.length - 1) closeGuide(true);
      else {
        currentStep += 1;
        renderGuide();
      }
    });
    guide.addEventListener('click', (event) => {
      if (event.target === guide) closeGuide(true);
    });

    if (!localStorage.getItem(storageKey)) window.setTimeout(openGuide, 450);
  }

  window.setTimeout(() => {
    document.querySelectorAll('[data-toast]').forEach((toast) => toast.remove());
  }, 5000);
})();
