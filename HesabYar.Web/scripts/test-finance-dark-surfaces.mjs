import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { existsSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { pathToFileURL, fileURLToPath } from 'node:url';
import { test } from 'node:test';

const browser = [
  process.env.CHROME_BIN,
  'C:/Program Files/Google/Chrome/Application/chrome.exe',
  'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  '/usr/bin/google-chrome',
  '/usr/bin/chromium'
].find((path) => path && existsSync(path));

const css = pathToFileURL(fileURLToPath(new URL('../wwwroot/css/app.css', import.meta.url))).href;
const fixture = `<!doctype html><html lang="fa" dir="rtl" data-finance-theme="dark"><head><meta charset="utf-8"><link rel="stylesheet" href="${css}"></head>
<body><div class="finance-shell"><aside class="finance-sidebar"><div class="sidebar-workspace"><span class="sidebar-workspace-label" id="workspace-label">فضای مالی</span><select id="workspace-select"><option>من</option></select></div></aside>
<div><header><a class="finance-alert-button bg-amber-50 text-amber-800" id="alert">اعلان</a></header><main>
<div class="stat" id="stat"><span class="stat-label" id="stat-label">تعهد این ماه</span><div class="stat-value" id="stat-value">۱۰۰ تومان</div></div>
<div class="finance-expenses-table table-wrap"><table class="table"><thead><tr><th id="table-heading">شرح</th></tr></thead><tbody><tr id="row-odd"><td>اول</td></tr><tr id="row-even"><td>دوم</td></tr></tbody></table></div>
<details open class="finance-budget-transfer bg-rose-50" id="transfer"><summary class="text-rose-700" id="transfer-title">جبران کسری</summary><p class="text-slate-600" id="transfer-copy">توضیح انتقال</p></details>
<div class="finance-obligation-payment bg-rose-50" id="payment"><label class="label" id="payment-label">مبلغ این پرداخت</label><p class="text-rose-700" id="payment-warning">سررسید گذشته</p></div>
</main></div></div><output id="result"></output><script>addEventListener('load', () => {
  const ids = ['workspace-label','workspace-select','alert','stat','stat-label','stat-value','table-heading','row-odd','row-even','transfer','transfer-title','transfer-copy','payment','payment-label','payment-warning'];
  document.querySelector('#result').textContent = JSON.stringify(Object.fromEntries(ids.map(id => {
    const style = getComputedStyle(document.getElementById(id));
    return [id, { color: style.color, background: style.backgroundColor }];
  })));
});</script></body></html>`;

function render(markup = fixture) {
  const directory = mkdtempSync(join(tmpdir(), 'donysh-dark-surfaces-'));
  try {
    const page = join(directory, 'fixture.html');
    writeFileSync(page, markup);
    const result = spawnSync(browser, ['--headless=new', '--disable-gpu', '--no-sandbox', '--disable-extensions',
      `--user-data-dir=${join(directory, 'profile')}`, '--virtual-time-budget=1000', '--dump-dom', pathToFileURL(page).href],
    { encoding: 'utf8', timeout: 20000 });
    assert.equal(result.status, 0, result.stderr);
    const match = result.stdout.match(/<output id="result">([^<]+)<\/output>/);
    assert.ok(match, result.stdout);
    return JSON.parse(match[1].replaceAll('&quot;', '"'));
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
}

function luminance(color) {
  const channels = color.match(/[\d.]+/g).slice(0, 3).map(Number).map(value => {
    const linear = value / 255;
    return linear <= 0.04045 ? linear / 12.92 : ((linear + 0.055) / 1.055) ** 2.4;
  });
  return channels[0] * 0.2126 + channels[1] * 0.7152 + channels[2] * 0.0722;
}

function contrast(foreground, background) {
  const values = [luminance(foreground), luminance(background)].sort((a, b) => b - a);
  return (values[0] + 0.05) / (values[1] + 0.05);
}

test('Finance dark surfaces keep readable labels and distinct expense rows', { skip: !browser }, () => {
  const styles = render();
  for (const [label, surface] of [
    ['workspace-label', 'workspace-select'], ['stat-label', 'stat'], ['stat-value', 'stat'],
    ['table-heading', 'table-heading'], ['transfer-title', 'transfer'], ['transfer-copy', 'transfer'],
    ['payment-label', 'payment'], ['payment-warning', 'payment']
  ]) {
    const ratio = contrast(styles[label].color, styles[surface].background);
    assert.ok(ratio >= 4.5, `${label} contrast ${ratio.toFixed(2)} is too low`);
  }
  assert.notEqual(styles['row-odd'].background, styles['row-even'].background);
  assert.notEqual(styles.alert.background, styles.stat.background);
});

test('dashboard alerts remain readable in dark mode', { skip: !browser }, () => {
  const markup = `<!doctype html><html lang="fa" dir="rtl" data-finance-theme="dark"><head><link rel="stylesheet" href="${css}"></head><body><div class="finance-shell finance-shell-dashboard"><main class="dashboard-alerts"><a class="dashboard-alert-danger" id="danger">کسری بودجه</a><a id="reminder">سررسید نزدیک</a></main></div><output id="result"></output><script>addEventListener('load',()=>{document.querySelector('#result').textContent=JSON.stringify(Object.fromEntries(['danger','reminder'].map(id=>{const style=getComputedStyle(document.getElementById(id));return[id,{color:style.color,background:style.backgroundColor}]})))})</script></body></html>`;
  const styles = render(markup);
  for (const id of ['danger', 'reminder']) {
    assert.ok(contrast(styles[id].color, styles[id].background) >= 4.5, `${id} lacks dark contrast`);
    assert.notEqual(styles[id].background, 'rgb(255, 248, 233)');
  }
});

test('budget, AI, workspaces and mobile status colors stay readable', { skip: !browser }, () => {
  const markup = `<!doctype html><html lang="fa" dir="rtl" data-finance-theme="dark"><head><link rel="stylesheet" href="${css}"></head><body><div class="finance-shell"><main>
  <span id="positive" class="budget-percent bg-emerald-100 text-emerald-700">۰٪</span><span id="warning" class="budget-percent bg-amber-100 text-amber-700">۸۰٪</span><span id="negative" class="budget-percent bg-rose-100 text-rose-700">۱۰۰٪</span>
  <p id="ai-warning" class="bg-amber-50 text-amber-800">هشدار دستیار</p><p id="ai-success" class="bg-emerald-50 text-emerald-800">تأیید دستیار</p>
  <p id="workspace-guide" class="bg-emerald-50 text-emerald-950">راهنمای فضای مشترک</p><p id="workspace-total" class="bg-indigo-50 text-indigo-900">مجموع بودجه</p>
  <span id="mobile-active" class="mobile-status is-active">فعال</span><span id="mobile-pending" class="mobile-status is-pending">در انتظار</span><div id="mobile-warning" class="mobile-duplicate-note">اتصال تکراری</div>
  </main></div><output id="result"></output><script>addEventListener('load',()=>{const ids=['positive','warning','negative','ai-warning','ai-success','workspace-guide','workspace-total','mobile-active','mobile-pending','mobile-warning'];document.querySelector('#result').textContent=JSON.stringify(Object.fromEntries(ids.map(id=>{const style=getComputedStyle(document.getElementById(id));return[id,{color:style.color,background:style.backgroundColor}]})))})</script></body></html>`;
  const styles = render(markup);
  for (const [id, style] of Object.entries(styles)) {
    assert.ok(contrast(style.color, style.background) >= 4.5, `${id} lacks dark contrast`);
  }
  assert.equal(new Set(['positive', 'warning', 'negative'].map(id => styles[id].background)).size, 3);
});

test('budget dialog opens for validation errors and can be closed and reopened', { skip: !browser }, () => {
  const script = pathToFileURL(fileURLToPath(new URL('../wwwroot/js/site.js', import.meta.url))).href;
  const markup = `<!doctype html><html lang="fa" dir="rtl" data-finance-theme="dark"><head><link rel="stylesheet" href="${css}"></head><body><button data-open-budget="budget-create-dialog">تعریف بودجه</button><dialog id="budget-create-dialog" class="budget-dialog" data-budget-error="true"><button data-close-budget>بستن</button><label class="budget-carryover-option"><strong>انتقال کسری به ماه بعد</strong></label></dialog><script src="${script}"></script><output id="result"></output><script>addEventListener('load',()=>{const dialog=document.querySelector('dialog');const initialOpen=dialog.open;document.querySelector('[data-close-budget]').click();const closed=!dialog.open;document.querySelector('[data-open-budget]').click();const reopened=dialog.open;const title=getComputedStyle(dialog.querySelector('strong'));const option=getComputedStyle(dialog.querySelector('label'));document.querySelector('#result').textContent=JSON.stringify({initialOpen,closed,reopened,title:{color:title.color},option:{background:option.backgroundColor}})})</script></body></html>`;
  const result = render(markup);
  assert.equal(result.initialOpen, true);
  assert.equal(result.closed, true);
  assert.equal(result.reopened, true);
  assert.ok(contrast(result.title.color, result.option.background) >= 4.5);
});

test('savings goal modal and its Persian date picker stay usable in dark mode', { skip: !browser }, () => {
  const script = pathToFileURL(fileURLToPath(new URL('../wwwroot/js/site.js', import.meta.url))).href;
  const markup = `<!doctype html><html lang="fa" dir="rtl" data-finance-theme="dark">
    <head><meta name="viewport" content="width=device-width,initial-scale=1"><link rel="stylesheet" href="${css}"></head>
    <body><div class="finance-shell"><main><div class="finance-savings">
      <button data-open-goal="savings-goal-dialog">هدف جدید</button>
      <div class="savings-goal-card card is-inactive"><span class="bg-indigo-100 text-indigo-700" id="status">فعال</span></div>
      <details class="bg-indigo-50" open><summary class="text-indigo-800" id="contribute">ثبت واریزی</summary></details>
      <dialog id="savings-goal-dialog" class="savings-goal-dialog" data-goal-error="true">
        <button data-close-goal>بستن</button><div style="height:500px"></div><label class="label">تاریخ هدف</label>
        <input data-persian-date value="1405/08/02" aria-label="تاریخ هدف">
      </dialog></div></main></div><script src="${script}"></script><output id="result"></output><script>
      addEventListener('load', () => {
        const dialog = document.querySelector('dialog');
        const opened = dialog.open;
        dialog.querySelector('.persian-datepicker-trigger').scrollIntoView();
        dialog.querySelector('.persian-datepicker-trigger').click();
        requestAnimationFrame(() => requestAnimationFrame(() => {
          const picker = dialog.querySelector('.persian-datepicker-popover');
          const pickerVisible = !picker.classList.contains('hidden');
          const rect = picker.getBoundingClientRect();
          const hit = document.elementFromPoint(rect.left + rect.width / 2, rect.top + rect.height / 2);
          const pickerClickable = !!hit?.closest('.persian-datepicker-popover');
          const style = id => { const value = getComputedStyle(document.getElementById(id)); return { color: value.color, background: value.backgroundColor }; };
          const status = style('status');
          const contribute = style('contribute');
          document.querySelector('[data-close-goal]').click();
          const closed = !dialog.open;
          document.querySelector('[data-open-goal]').click();
          dialog.querySelector('.persian-datepicker-trigger').click();
          document.querySelector('#result').textContent = JSON.stringify({ opened, closed, reopened: dialog.open,
            pickerVisible, pickerInsideDialog: picker.parentElement === dialog,
            pickerClickable, pickerReopened: !picker.classList.contains('hidden'), hit: hit?.outerHTML.slice(0, 180), viewport: [innerWidth, innerHeight], rect: rect.toJSON(), status, contribute });
        }));
      });
    </script></body></html>`;
  const result = render(markup);
  assert.equal(result.opened, true);
  assert.equal(result.closed, true);
  assert.equal(result.reopened, true);
  assert.equal(result.pickerVisible, true);
  assert.equal(result.pickerInsideDialog, true);
  assert.equal(result.pickerClickable, true, JSON.stringify({ hit: result.hit, viewport: result.viewport, rect: result.rect }));
  assert.equal(result.pickerReopened, true);
  assert.ok(contrast(result.status.color, result.status.background) >= 4.5);
  assert.ok(contrast(result.contribute.color, result.contribute.background) >= 4.5);
});
