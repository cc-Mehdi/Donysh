import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import { runInNewContext } from 'node:vm';

const source = readFileSync(new URL('../wwwroot/js/finance-theme-init.js', import.meta.url), 'utf8');

function createPage(initialHour, previousSlogan = null) {
  const clock = { value: new Date(2026, 9, 23, initialHour).getTime() };
  const values = new Map();
  if (previousSlogan !== null) values.set('donysh-finance-last-slogan', String(previousSlogan));
  const listeners = new Map();
  const greeting = { textContent: '' };
  const slogan = { textContent: '' };
  const toggleButton = { attributes: {}, setAttribute(name, value) { this.attributes[name] = value; } };
  const root = { dataset: {} };
  const document = {
    documentElement: root,
    hidden: false,
    querySelector: (selector) => selector === '[data-dashboard-slogan]' ? slogan : null,
    querySelectorAll: (selector) => selector === '[data-dashboard-greeting]'
      ? [greeting]
      : selector === '[data-finance-theme-toggle]' ? [toggleButton] : [],
    addEventListener: (name, callback) => listeners.set(name, callback)
  };
  const localStorage = {
    getItem: (key) => values.get(key) ?? null,
    setItem: (key, value) => values.set(key, value),
    removeItem: (key) => values.delete(key)
  };
  const FakeDate = class extends Date {
    constructor(...args) { super(...(args.length ? args : [clock.value])); }
    static now() { return clock.value; }
  };
  const fakeMath = Object.create(Math);
  fakeMath.random = () => 0;
  const window = { setInterval() {} };
  runInNewContext(source, {
    Date: FakeDate,
    document,
    localStorage,
    window,
    Math: fakeMath,
    console
  });
  return {
    root, greeting, slogan, toggleButton, values,
    refresh: window.donyshFinanceTheme.refresh,
    toggle: window.donyshFinanceTheme.toggle,
    ready: () => listeners.get('DOMContentLoaded')(),
    setHour: (hour) => { clock.value = new Date(2026, 9, 23, hour).getTime(); }
  };
}

test('local time selects day, evening, and night greetings and themes', () => {
  const page = createPage(5);
  assert.equal(page.root.dataset.financeTheme, 'dark');
  assert.equal(page.greeting.textContent, 'شب بخیر');
  page.setHour(6);
  page.refresh();
  assert.equal(page.root.dataset.financeTheme, 'light');
  assert.equal(page.greeting.textContent, 'روز بخیر');
  page.setHour(17);
  page.refresh();
  assert.equal(page.root.dataset.financeTheme, 'light');
  assert.equal(page.greeting.textContent, 'عصر بخیر');
  page.setHour(18);
  page.refresh();
  assert.equal(page.root.dataset.financeTheme, 'dark');
  assert.equal(page.greeting.textContent, 'عصر بخیر');
  page.setHour(20);
  page.refresh();
  assert.equal(page.root.dataset.financeTheme, 'dark');
  assert.equal(page.greeting.textContent, 'شب بخیر');
  assert.equal(page.toggleButton.attributes['aria-pressed'], 'true');
});

test('manual theme choice expires at the next day or night boundary', () => {
  const page = createPage(8);
  page.toggle();
  assert.equal(page.root.dataset.financeTheme, 'dark');
  assert.ok(page.values.has('donysh-finance-theme-override'));
  page.setHour(18);
  page.refresh();
  assert.equal(page.root.dataset.financeTheme, 'dark');
  assert.equal(page.values.has('donysh-finance-theme-override'), false);
  page.setHour(6);
  page.refresh();
  assert.equal(page.root.dataset.financeTheme, 'light');
});

test('a new visit picks a short slogan different from the previous one', () => {
  const page = createPage(8, 0);
  page.ready();
  assert.equal(page.slogan.textContent, 'قدم‌های کوچک، آرامش مالی می‌سازند.');
  assert.equal(page.values.get('donysh-finance-last-slogan'), '1');
});
