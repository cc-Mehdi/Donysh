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

function render() {
  const directory = mkdtempSync(join(tmpdir(), 'donysh-dark-surfaces-'));
  try {
    const page = join(directory, 'fixture.html');
    writeFileSync(page, fixture);
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
