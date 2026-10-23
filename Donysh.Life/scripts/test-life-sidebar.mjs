import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { runInNewContext } from 'node:vm';

const source = readFileSync(new URL('../wwwroot/js/life-sidebar.js', import.meta.url), 'utf8');

function createPage(initialWidth, storage = new Map()) {
    const events = new Map();
    const attributes = new Map();
    const button = {
        setAttribute(name, value) { attributes.set(name, value); },
        addEventListener(name, callback) { events.set(name, callback); }
    };
    const media = { matches: initialWidth >= 901 && initialWidth <= 1200, addEventListener(name, callback) { events.set(`media:${name}`, callback); } };
    const document = {
        documentElement: { dataset: {} },
        querySelector: () => button,
        addEventListener(name, callback) { events.set(name, callback); }
    };
    const localStorage = {
        getItem(key) { return storage.get(key) ?? null; },
        setItem(key, value) { storage.set(key, value); }
    };
    runInNewContext(source, { document, window: { matchMedia: () => media }, localStorage });
    return {
        document, attributes, storage,
        ready() { events.get('DOMContentLoaded')(); },
        click() { events.get('click')(); },
        resize(width) { media.matches = width >= 901 && width <= 1200; events.get('media:change')(); }
    };
}

test('medium widths start compact and the toggle exposes the full menu', () => {
    const page = createPage(1050);
    assert.equal(page.document.documentElement.dataset.lifeSidebar, 'collapsed');
    page.ready();
    assert.equal(page.attributes.get('aria-expanded'), 'false');
    page.click();
    assert.equal(page.document.documentElement.dataset.lifeSidebar, 'expanded');
    assert.equal(page.attributes.get('aria-expanded'), 'true');
    assert.equal(page.storage.get('donysh-life-sidebar-compact'), 'expanded');
});

test('wide and medium preferences remain independent across resizes and visits', () => {
    const storage = new Map();
    const page = createPage(1450, storage);
    page.ready();
    assert.equal(page.document.documentElement.dataset.lifeSidebar, 'expanded');
    page.click();
    assert.equal(storage.get('donysh-life-sidebar-wide'), 'collapsed');
    page.resize(1050);
    assert.equal(page.document.documentElement.dataset.lifeSidebar, 'collapsed');
    page.click();
    page.resize(1450);
    assert.equal(page.document.documentElement.dataset.lifeSidebar, 'collapsed');
    page.resize(1050);
    assert.equal(page.document.documentElement.dataset.lifeSidebar, 'expanded');
    assert.equal(createPage(1050, storage).document.documentElement.dataset.lifeSidebar, 'expanded');
});

const chrome = [
    process.env.CHROME_BIN,
    'C:/Program Files/Google/Chrome/Application/chrome.exe',
    'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
    '/usr/bin/google-chrome',
    '/usr/bin/chromium'
].find((path) => path && existsSync(path));
const css = pathToFileURL(fileURLToPath(new URL('../wwwroot/css/app.css', import.meta.url))).href;
const mark = pathToFileURL(fileURLToPath(new URL('../wwwroot/images/donysh-life-mark.svg', import.meta.url))).href;
const fixture = `<!doctype html><html lang="fa" dir="rtl"><head><meta charset="utf-8"><link rel="stylesheet" href="${css}"><script>
document.documentElement.dataset.lifeSidebar = new URLSearchParams(location.search).get('state');</script></head><body>
<div class="app-shell"><aside class="sidebar"><div class="sidebar-header"><a class="brand life-brand"><img class="life-brand-logo" src="${mark}" width="44" height="44"><strong class="life-brand-name">دُنیش زندگی</strong></a><button class="sidebar-toggle"><svg class="sidebar-toggle-collapse" viewBox="0 0 24 24"><rect x="3" y="3" width="18" height="18" rx="2"/><path d="M15 3v18m-7-12 3 3-3 3"/></svg><svg class="sidebar-toggle-expand" viewBox="0 0 24 24"><rect x="3" y="3" width="18" height="18" rx="2"/><path d="M15 3v18M10 9l-3 3 3 3"/></svg></button></div>
<p class="sidebar-caption">زمانت را آگاهانه زندگی کن</p><nav class="side-nav"><a><span>⌂</span><span class="nav-label">امروز</span></a><a><span>◎</span><span class="nav-label">فعالیت‌ها</span></a><a><span>▦</span><span class="nav-label">برنامهٔ هفته</span></a><a><span>✦</span><span class="nav-label">دستیار هوشمند</span></a><a><span class="nav-icon"><svg viewBox="0 0 24 24"><path d="M3 3v18h18M7 17l4-5 3 2 5-7m-4 0h4v4"/></svg></span><span class="nav-label">پیشرفت</span></a></nav>
<div class="sidebar-footer"><section class="account-card"><span class="account-avatar">د</span><span class="account-copy">کاربر</span></section><a class="product-switcher"><span class="product-switcher-icon">◉</span><span class="product-switcher-copy">مالی</span></a><form class="logout-form"><button><span class="logout-icon">⇥</span><span class="logout-label">خروج امن</span></button></form></div></aside>
<div class="page-column"><header class="mobile-header">دنیش زندگی</header><main class="content-shell">محتوا</main><nav class="mobile-nav"><form class="mobile-logout-form"><button><span><svg viewBox="0 0 24 24"><path d="m16 17 5-5-5-5M21 12H9M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"/></svg></span><small>خروج</small></button></form></nav></div></div>
<output id="result"></output><script>addEventListener('load', () => {
const sidebar = document.querySelector('.sidebar'), nav = document.querySelector('.side-nav'), footer = document.querySelector('.sidebar-footer');
document.querySelector('#result').textContent = JSON.stringify({ width: innerWidth, sidebarWidth: sidebar.getBoundingClientRect().width,
sidebarDisplay: getComputedStyle(sidebar).display, labelDisplay: getComputedStyle(document.querySelector('.nav-label')).display,
expandIconDisplay: getComputedStyle(document.querySelector('.sidebar-toggle-expand')).display,
collapseIconDisplay: getComputedStyle(document.querySelector('.sidebar-toggle-collapse')).display,
brandLogoDisplay: getComputedStyle(document.querySelector('.sidebar .life-brand-logo')).display,
brandLogoWidth: document.querySelector('.sidebar .life-brand-logo').getBoundingClientRect().width,
progressIconWidth: document.querySelector('.side-nav .nav-icon svg').getBoundingClientRect().width,
mobileLogoutIconWidth: document.querySelector('.mobile-logout-form svg').getBoundingClientRect().width,
footerBelowNav: footer.getBoundingClientRect().top >= nav.getBoundingClientRect().bottom,
mobileNavDisplay: getComputedStyle(document.querySelector('.mobile-nav')).display });
});</script></body></html>`;

function render(width, state) {
    const temporary = mkdtempSync(join(tmpdir(), 'donysh-life-sidebar-'));
    try {
        const page = join(temporary, 'sidebar.html');
        writeFileSync(page, fixture);
        const result = spawnSync(chrome, ['--headless=new', '--disable-gpu', '--no-sandbox', '--disable-extensions',
            `--user-data-dir=${join(temporary, 'profile')}`, `--window-size=${width},700`, '--virtual-time-budget=1000',
            '--dump-dom', `${pathToFileURL(page).href}?state=${state}`], { encoding: 'utf8', timeout: 20000 });
        assert.equal(result.status, 0, result.stderr);
        const match = result.stdout.match(/<output id="result">([^<]+)<\/output>/);
        assert.ok(match, result.stdout);
        return JSON.parse(match[1].replaceAll('&quot;', '"'));
    } finally {
        rmSync(temporary, { recursive: true, force: true });
    }
}

test('sidebar uses a 64px icon rail and preserves the mobile navigation', { skip: !chrome }, () => {
    const wide = render(1400, 'expanded');
    const compact = render(1100, 'collapsed');
    const mediumExpanded = render(1100, 'expanded');
    const mobile = render(800, 'collapsed');
    assert.equal(wide.sidebarWidth, 236);
    assert.notEqual(wide.labelDisplay, 'none');
    assert.equal(compact.sidebarWidth, 64);
    assert.equal(compact.labelDisplay, 'none');
    assert.notEqual(compact.expandIconDisplay, 'none');
    assert.equal(compact.collapseIconDisplay, 'none');
    assert.notEqual(wide.collapseIconDisplay, 'none');
    assert.notEqual(compact.brandLogoDisplay, 'none');
    assert.equal(compact.brandLogoWidth, wide.brandLogoWidth);
    assert.ok(compact.progressIconWidth > 0);
    assert.equal(compact.footerBelowNav, true);
    assert.equal(mediumExpanded.sidebarWidth, 236);
    assert.equal(mobile.sidebarDisplay, 'none');
    assert.equal(mobile.mobileNavDisplay, 'grid');
    assert.ok(mobile.mobileLogoutIconWidth > 0);
});
