import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { existsSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { pathToFileURL, fileURLToPath } from 'node:url';
import { test } from 'node:test';

const chrome = [
  process.env.CHROME_BIN,
  'C:/Program Files/Google/Chrome/Application/chrome.exe',
  'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  '/usr/bin/google-chrome',
  '/usr/bin/chromium'
].find((path) => path && existsSync(path));

const css = pathToFileURL(fileURLToPath(new URL('../wwwroot/css/app.css', import.meta.url))).href;
const links = Array.from({ length: 11 }, (_, i) => `<a class="nav-link" href="#${i}">گزینهٔ ${i + 1}</a>`).join('');
const fixture = `<!doctype html><html lang="fa" dir="rtl"><head><meta charset="utf-8"><link rel="stylesheet" href="${css}"></head>
<body class="finance-body"><div class="finance-shell min-h-screen lg:grid lg:grid-cols-[17rem_1fr]"><aside id="sidebar" class="finance-sidebar fixed inset-y-0 right-0 z-40 w-72 border-l border-slate-200 bg-white p-5 lg:static lg:block lg:w-auto">
<div class="sidebar-brand-row mb-7 flex items-center justify-between"><span>دُنیش مالی</span><button class="sidebar-collapse-button hidden lg:grid" aria-label="جمع‌کردن سایدبار"><svg class="lucide-icon sidebar-collapse-icon" viewBox="0 0 24 24"><rect x="3" y="3" width="18" height="18" rx="2"/><path d="M15 3v18m-7-12 3 3-3 3"/></svg><svg class="lucide-icon sidebar-expand-icon" viewBox="0 0 24 24"><rect x="3" y="3" width="18" height="18" rx="2"/><path d="M9 3v18m7-12-3 3 3 3"/></svg></button></div>
<div class="sidebar-workspace mb-6 rounded-2xl border border-indigo-100 bg-indigo-50 p-3">فضای مالی</div><nav class="space-y-1">${links}</nav>
<div class="sidebar-footer mt-4 lg:mt-10"><button class="nav-link sidebar-logout w-full">خروج از حساب کاربری</button></div></aside>
<div class="finance-content-scroll min-w-0"><main class="dashboard-main" style="height: 1800px"><section class="dashboard-hero"><div class="dashboard-hero-top"><div class="dashboard-hero-start"><div class="dashboard-hero-date-row"><button class="dashboard-icon-button dashboard-menu-button">☰</button><span class="dashboard-date">امروز</span></div><div class="dashboard-greeting"><h1>عصر بخیر</h1><p>خرج‌ها را که ببینی، تصمیم‌ها روشن‌تر می‌شوند.</p></div></div><div class="dashboard-hero-tools"><button class="dashboard-icon-button" data-finance-theme-toggle>◐</button><a class="dashboard-icon-button dashboard-account-link" aria-label="مدیریت حساب"><svg class="lucide-icon" viewBox="0 0 24 24"><circle cx="12" cy="12" r="9"/><circle cx="12" cy="10" r="3"/></svg></a><a class="hero-action hero-action-primary hero-header-expense">ثبت خرج</a></div></div></section></main></div></div>
<output id="result"></output><script>if (location.search.includes('collapsed=1')) document.documentElement.dataset.financeSidebar = 'collapsed';
addEventListener('load', () => {
 const sidebar = document.querySelector('#sidebar'), content = document.querySelector('.finance-content-scroll'), nav = document.querySelector('nav'), footer = document.querySelector('.sidebar-footer'), icon = document.querySelector('.sidebar-collapse-icon'), expandIcon = document.querySelector('.sidebar-expand-icon');
 const date = document.querySelector('.dashboard-date'), greeting = document.querySelector('.dashboard-greeting');
 const account = document.querySelector('.dashboard-account-link'), theme = document.querySelector('[data-finance-theme-toggle]'), person = account.querySelector('svg');
 content.scrollTop = 160; sidebar.scrollTop = 80;
 document.querySelector('#result').textContent = JSON.stringify({ width: innerWidth, display: getComputedStyle(sidebar).display,
  overflow: getComputedStyle(sidebar).overflowY, scrollable: sidebar.scrollHeight > sidebar.clientHeight,
  sidebarScrollTop: sidebar.scrollTop, contentScrollTop: content.scrollTop, contentOverflow: getComputedStyle(content).overflowY,
  contentScrollable: content.scrollHeight > content.clientHeight, bodyScrollTop: document.scrollingElement.scrollTop,
  sidebarHeight: sidebar.getBoundingClientRect().height, contentHeight: content.getBoundingClientRect().height,
  scrollbarWidth: getComputedStyle(sidebar).scrollbarWidth,
  greetingTop: greeting.getBoundingClientRect().top, dateBottom: date.getBoundingClientRect().bottom,
  greetingRight: greeting.getBoundingClientRect().right, dateRight: date.getBoundingClientRect().right,
  greetingBackground: getComputedStyle(greeting).backgroundColor,
  accountTop: account.getBoundingClientRect().top, accountHeight: account.getBoundingClientRect().height,
  themeTop: theme.getBoundingClientRect().top, themeHeight: theme.getBoundingClientRect().height,
  personCenter: person.getBoundingClientRect().top + person.getBoundingClientRect().height / 2,
  navBottom: nav.getBoundingClientRect().bottom, footerTop: footer.getBoundingClientRect().top,
  iconDisplay: getComputedStyle(icon).display, iconWidth: icon.getBoundingClientRect().width, iconShapeWidth: icon.getBBox().width,
  expandIconDisplay: getComputedStyle(expandIcon).display, expandIconWidth: expandIcon.getBoundingClientRect().width });
});</script></body></html>`;

function render(width, height, collapsed = false) {
  const temporary = mkdtempSync(join(tmpdir(), 'donysh-sidebar-test-'));
  try {
    const page = join(temporary, 'sidebar.html');
    writeFileSync(page, fixture);
    const result = spawnSync(chrome, ['--headless=new', '--disable-gpu', '--no-sandbox', '--disable-extensions',
      `--user-data-dir=${join(temporary, 'profile')}`, `--window-size=${width},${height}`, '--virtual-time-budget=1000',
      '--dump-dom', `${pathToFileURL(page).href}${collapsed ? '?collapsed=1' : ''}`], { encoding: 'utf8', timeout: 20000 });
    assert.equal(result.status, 0, result.stderr);
    const match = result.stdout.match(/<output id="result">([^<]+)<\/output>/);
    assert.ok(match, result.stdout);
    return JSON.parse(match[1].replaceAll('&quot;', '"'));
  } finally {
    rmSync(temporary, { recursive: true, force: true });
  }
}

test('mobile sidebar scrolls without covering the last menu item', { skip: !chrome }, () => {
  const layout = render(390, 640);
  assert.ok(layout.width < 1024);
  assert.equal(layout.display, 'flex');
  assert.equal(layout.overflow, 'auto');
  assert.equal(layout.scrollable, true);
  assert.ok(layout.navBottom <= layout.footerTop);
});

test('desktop collapse control has a visible icon', { skip: !chrome }, () => {
  const layout = render(1280, 800);
  assert.ok(layout.width >= 1024);
  assert.notEqual(layout.iconDisplay, 'none');
  assert.ok(layout.iconWidth >= 20);
  assert.ok(layout.iconShapeWidth > 0);
  assert.equal(layout.expandIconDisplay, 'none');
  const collapsed = render(1280, 800, true);
  assert.equal(collapsed.iconDisplay, 'none');
  assert.notEqual(collapsed.expandIconDisplay, 'none');
  assert.ok(collapsed.expandIconWidth >= 20);
});

test('desktop sidebar and page content scroll independently within the viewport', { skip: !chrome }, () => {
  const layout = render(1280, 400);
  assert.equal(layout.overflow, 'auto');
  assert.equal(layout.contentOverflow, 'auto');
  assert.equal(layout.scrollable, true);
  assert.equal(layout.contentScrollable, true);
  assert.ok(layout.sidebarScrollTop > 0);
  assert.equal(layout.contentScrollTop, 160);
  assert.equal(layout.bodyScrollTop, 0);
  assert.ok(layout.sidebarHeight <= 400);
  assert.ok(layout.contentHeight <= 400);
  assert.equal(layout.scrollbarWidth, 'thin');
});

test('dashboard greeting stays below the date with an opaque reading surface', { skip: !chrome }, () => {
  for (const width of [1280, 500]) {
    const layout = render(width, 800);
    assert.ok(layout.greetingTop >= layout.dateBottom);
    assert.ok(Math.abs(layout.greetingRight - layout.dateRight) <= 1);
    assert.notEqual(layout.greetingBackground, 'rgba(0, 0, 0, 0)');
  }
});

test('dashboard account control aligns with the theme control and centers its icon', { skip: !chrome }, () => {
  const layout = render(1280, 800);
  assert.ok(Math.abs(layout.accountTop - layout.themeTop) <= 1);
  assert.ok(Math.abs(layout.accountHeight - layout.themeHeight) <= 1);
  assert.ok(Math.abs(layout.personCenter - (layout.accountTop + layout.accountHeight / 2)) <= 1);
});
