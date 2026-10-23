import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { existsSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { test } from 'node:test';

const browser = [
  process.env.CHROME_BIN,
  'C:/Program Files/Google/Chrome/Application/chrome.exe',
  'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  '/usr/bin/google-chrome',
  '/usr/bin/chromium'
].find(path => path && existsSync(path));
const css = pathToFileURL(fileURLToPath(new URL('../wwwroot/css/app.css', import.meta.url))).href;
const siteJs = pathToFileURL(fileURLToPath(new URL('../wwwroot/js/site.js', import.meta.url))).href;

function inspect(width, theme) {
  const directory = mkdtempSync(join(tmpdir(), 'donysh-report-layout-'));
  try {
    const html = `<!doctype html><html lang="fa" dir="rtl" data-finance-theme="${theme}"><head><meta name="viewport" content="width=device-width,initial-scale=1"><link rel="stylesheet" href="${css}"></head><body><div class="finance-shell"><main><div class="finance-reports">
      <header class="report-hero"><div><h2>گزارش مالی</h2><p>مرور وضعیت مالی</p></div><form class="report-filter"><div><label>بازه</label><select><option>ماهانه</option></select></div><div class="report-date-field"><label>تاریخ</label><input value="۱۴۰۵/۰۸/۰۱" data-persian-date></div><button type="submit">نمایش گزارش</button></form></header>
      <div class="report-toolbar"><nav class="report-period-nav"><a>→</a><strong>آبان ۱۴۰۵</strong><a>←</a></nav><nav class="report-tabs"><a href="#report-overview">نمای کلی</a><a href="#report-categories">دسته‌ها</a><a href="#report-obligations">اقساط</a><a href="#report-members">اعضا</a></nav></div>
      <section class="report-metrics"><article class="report-metric report-metric-violet"><span>بیشترین دسته</span><strong>خوراک</strong></article><article class="report-metric report-metric-blue"><span>تعداد خرج‌ها</span><strong>۱۲</strong></article><article class="report-metric report-metric-pink"><span>مجموع مخارج</span><strong>۷٬۲۴۵٬۰۰۰</strong></article><article class="report-metric report-metric-teal"><span>میانگین روزانه</span><strong>۲۴۰٬۰۰۰</strong></article></section>
      <div class="report-primary-grid"><section class="report-panel report-trend"><div class="report-panel-head"><h3>روند مخارج</h3></div><div class="report-chart-scroll"><svg class="report-line-chart" viewBox="0 0 660 222"><path class="report-chart-grid" d="M30 40H630 M30 88H630"/><polyline class="report-line-expense" points="30,180 330,90 630,130"/></svg></div></section><section class="report-panel"><h3>دسته‌های اصلی</h3><div class="report-category-content"><div class="report-donut" style="background:conic-gradient(#795cff 0 55%,#e96bbf 55% 100%)"><div><strong>۷٬۲۴۵٬۰۰۰</strong><span>تومان خرج</span></div></div><ul class="report-category-list"><li><span>خوراک</span><strong>۵۵٪</strong></li></ul></div></section></div>
      <div class="report-obligation-grid"><article class="report-panel report-obligation-summary"><h4>وضعیت پرداخت</h4><div class="report-obligation-summary-body"><div class="report-gauge" style="--progress:60%"><strong>۶۰٪</strong></div><div class="report-obligation-totals"><div><span>تعهد ماه</span><strong>۱۰۰٬۰۰۰</strong></div></div></div></article><article class="report-panel"><h4>سررسیدها</h4></article><article class="report-panel"><h4>تقویم</h4></article></div>
      <div class="report-members-grid"><article class="report-panel"><h4>عملکرد اعضا</h4></article><article class="report-panel"><h4>جزئیات اعضا</h4></article></div>
      </div></main></div><output id="result"></output><script src="${siteJs}"></script><script>addEventListener('load',()=>{history.replaceState(null,'','#report-members');dispatchEvent(new Event('hashchange'));const get=s=>getComputedStyle(document.querySelector(s));const input=document.querySelector('[data-persian-date]');const trigger=document.querySelector('.persian-datepicker-trigger');const inputRect=input.getBoundingClientRect();const triggerRect=trigger.getBoundingClientRect();const measure=document.createElement('canvas').getContext('2d');measure.font=getComputedStyle(input).font;const textLeft=inputRect.right-parseFloat(getComputedStyle(input).paddingRight)-measure.measureText(input.value).width;const dateGap=textLeft-triggerRect.right;trigger.click();const picker=document.querySelector('.persian-datepicker-popover');const month=picker.querySelector('[data-pdp-month]');const initialMonth=month.value;picker.querySelector('[data-pdp-prev]').click();document.querySelector('#result').textContent=JSON.stringify({viewport:innerWidth,bodyWidth:document.body.scrollWidth,metrics:get('.report-metrics').gridTemplateColumns.split(' ').length,primary:get('.report-primary-grid').gridTemplateColumns.split(' ').length,obligations:get('.report-obligation-grid').gridTemplateColumns.split(' ').length,heroDirection:get('.report-hero').flexDirection,heroColor:get('.report-hero').color,panelColor:get('.report-panel').color,panelBackground:get('.report-panel').backgroundColor,metricColor:get('.report-metric').color,tabCurrent:document.querySelector('.report-tabs [aria-current]').hash,dateGap,triggerWidth:triggerRect.width,triggerBackground:get('.persian-datepicker-trigger').backgroundColor,submitBackground:get('.report-filter > button[type=submit]').backgroundColor,navIcons:picker.querySelectorAll('.persian-datepicker-nav svg').length,monthChanged:month.value!==initialMonth})})</script></body></html>`;
    const file = join(directory, 'report.html');
    writeFileSync(file, html);
    const result = spawnSync(browser, ['--headless=new', '--disable-gpu', '--no-sandbox', '--disable-extensions',
      `--user-data-dir=${join(directory, 'profile')}`, `--window-size=${width},900`, '--virtual-time-budget=1000',
      '--dump-dom', pathToFileURL(file).href], { encoding: 'utf8', timeout: 20000 });
    assert.equal(result.status, 0, result.stderr);
    const match = result.stdout.match(/<output id="result">([^<]+)<\/output>/);
    assert.ok(match, result.stdout);
    return JSON.parse(match[1].replaceAll('&quot;', '"'));
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
}

test('financial report uses a readable dark desktop dashboard grid', { skip: !browser }, () => {
  const page = inspect(1440, 'dark');
  assert.equal(page.metrics, 4);
  assert.equal(page.primary, 2);
  assert.equal(page.obligations, 3);
  assert.equal(page.metricColor, 'rgb(255, 255, 255)');
  assert.notEqual(page.panelColor, page.panelBackground);
  assert.equal(page.tabCurrent, '#report-members');
});

test('financial report stacks cards and charts at phone width in both themes', { skip: !browser }, () => {
  for (const theme of ['light', 'dark']) {
    const page = inspect(390, theme);
    assert.equal(page.metrics, 2);
    assert.equal(page.primary, 1);
    assert.equal(page.obligations, 1);
    assert.equal(page.heroDirection, 'column');
    assert.ok(page.bodyWidth <= page.viewport + 2, `${theme} report overflows ${page.bodyWidth}px viewport`);
  }
});

test('financial report chart adapts to a narrow content column', { skip: !browser }, () => {
  const page = inspect(1024, 'dark');
  assert.equal(page.primary, 1);
  assert.ok(page.bodyWidth <= page.viewport + 2);
});

test('report date control keeps its icon clear of the date and month controls use icons', { skip: !browser }, () => {
  for (const width of [390, 1440]) {
    for (const theme of ['light', 'dark']) {
      const page = inspect(width, theme);
      assert.ok(page.dateGap >= 8, `${theme} ${width}px date overlaps the calendar icon by ${page.dateGap}px`);
      assert.equal(page.triggerWidth, 32);
      assert.notEqual(page.triggerBackground, page.submitBackground);
      assert.equal(page.navIcons, 2);
      assert.equal(page.monthChanged, true);
    }
  }
});
