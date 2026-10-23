import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { runInNewContext } from 'node:vm';

const source = readFileSync(new URL('../wwwroot/js/life-theme.js', import.meta.url), 'utf8');

function page({ darkSystem = false, saved = null } = {}) {
    const events = new Map();
    const attributes = new Map();
    const storage = new Map(saved ? [['donysh-life-theme', saved]] : []);
    const buttons = Array.from({ length: 2 }, () => ({
        label: { textContent: '' },
        attributes: new Map(),
        setAttribute(name, value) { this.attributes.set(name, value); },
        querySelector: () => null,
        addEventListener(name, callback) { events.set(`button:${events.size}`, callback); }
    }));
    buttons[0].querySelector = () => buttons[0].label;
    const media = { matches: darkSystem, addEventListener(name, callback) { events.set(`media:${name}`, callback); } };
    const document = {
        documentElement: { dataset: {} },
        querySelector: () => ({ setAttribute(name, value) { attributes.set(name, value); } }),
        querySelectorAll: () => buttons,
        addEventListener(name, callback) { events.set(name, callback); }
    };
    runInNewContext(source, {
        document,
        window: { matchMedia: () => media },
        localStorage: { getItem: key => storage.get(key) ?? null, setItem: (key, value) => storage.set(key, value) }
    });
    return {
        document, buttons, storage, attributes,
        ready() { events.get('DOMContentLoaded')(); },
        toggle() { [...events.entries()].find(([key]) => key.startsWith('button:'))[1](); },
        system(dark) { media.matches = dark; events.get('media:change')(); }
    };
}

test('system preference applies before rendering and updates while no manual choice exists', () => {
    const view = page({ darkSystem: true });
    assert.equal(view.document.documentElement.dataset.lifeTheme, 'dark');
    view.ready();
    assert.equal(view.buttons[0].attributes.get('aria-pressed'), 'true');
    assert.equal(view.buttons[0].label.textContent, 'حالت روشن');
    view.system(false);
    assert.equal(view.document.documentElement.dataset.lifeTheme, 'light');
    assert.equal(view.attributes.get('content'), '#0d9488');
});

test('manual choice persists across pages and both toggles stay synchronized', () => {
    const view = page();
    view.ready();
    view.toggle();
    assert.equal(view.document.documentElement.dataset.lifeTheme, 'dark');
    assert.equal(view.storage.get('donysh-life-theme'), 'dark');
    assert.ok(view.buttons.every(button => button.attributes.get('aria-pressed') === 'true'));
    view.system(false);
    assert.equal(view.document.documentElement.dataset.lifeTheme, 'dark');
    const nextPage = page({ saved: 'dark' });
    assert.equal(nextPage.document.documentElement.dataset.lifeTheme, 'dark');
});

const chrome = [
    process.env.CHROME_BIN,
    'C:/Program Files/Google/Chrome/Application/chrome.exe',
    'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'
].find(path => path && existsSync(path));

test('activity swatches keep distinct colors and scrollbars follow each theme', { skip: !chrome }, () => {
    const temporary = mkdtempSync(join(tmpdir(), 'donysh-life-colors-'));
    try {
        const css = pathToFileURL(fileURLToPath(new URL('../wwwroot/css/app.css', import.meta.url))).href;
        const fixture = `<!doctype html><html lang="fa" dir="rtl" data-life-theme="dark"><head><link rel="stylesheet" href="${css}"></head><body><aside class="sidebar">منو</aside><main><label class="color-option"><input type="radio" name="color" checked><span class="color-option-face activity-teal"></span></label><label class="color-option"><input type="radio" name="color"><span class="color-option-face activity-blue"></span></label><label class="color-option"><input type="radio" name="color"><span class="color-option-face activity-orange"></span></label></main><output id="result"></output><script>addEventListener('load',()=>{const swatches=[...document.querySelectorAll('.color-option-face')];const read=()=>({colors:swatches.map(s=>getComputedStyle(s).backgroundColor),pageScroll:getComputedStyle(document.documentElement).scrollbarColor,sidebarScroll:getComputedStyle(document.querySelector('.sidebar')).scrollbarColor});const dark=read();document.documentElement.dataset.lifeTheme='light';document.querySelector('#result').textContent=JSON.stringify({dark,light:read()})})</script></body></html>`;
        const file = join(temporary, 'colors.html');
        writeFileSync(file, fixture);
        const result = spawnSync(chrome, ['--headless=new', '--disable-gpu', '--no-sandbox', '--disable-extensions',
            `--user-data-dir=${join(temporary, 'profile')}`, '--virtual-time-budget=1000',
            '--dump-dom', pathToFileURL(file).href], { encoding: 'utf8', timeout: 20000 });
        assert.equal(result.status, 0, result.stderr);
        const match = result.stdout.match(/<output id="result">([^<]+)<\/output>/);
        assert.ok(match, result.stdout);
        const actual = JSON.parse(match[1].replaceAll('&quot;', '"'));
        assert.equal(new Set(actual.dark.colors).size, 3);
        assert.deepEqual(actual.dark.colors, actual.light.colors);
        assert.notEqual(actual.dark.pageScroll, actual.light.pageScroll);
        assert.notEqual(actual.light.sidebarScroll, actual.light.pageScroll);
    } finally {
        rmSync(temporary, { recursive: true, force: true });
    }
});

test('dark palette reaches the shell, content, form, calendar and account surfaces', { skip: !chrome }, () => {
    const temporary = mkdtempSync(join(tmpdir(), 'donysh-life-theme-'));
    try {
        const css = pathToFileURL(fileURLToPath(new URL('../wwwroot/css/app.css', import.meta.url))).href;
        const fixture = `<!doctype html><html lang="fa" dir="rtl" data-life-theme="dark"><head><link rel="stylesheet" href="${css}"></head><body><aside class="sidebar"><button class="theme-toggle">حالت روشن</button></aside><main><article class="panel">متن</article><article class="auth-card">حساب</article><article class="metric-card"><span>هدف هفته</span><strong>۶ ساعت</strong></article><article class="today-stat">امروز</article><article class="template-card">راه‌اندازی</article><article class="activity-card">فعالیت</article><article class="progress-activity">پیشرفت</article><article class="ai-prompt-box">دستیار</article><div class="calendar-board">تقویم</div><dialog class="activity-create-dialog">افزودن فعالیت</dialog><label class="field">عنوان<input placeholder="عنوان"></label></main><output id="result"></output><script>addEventListener('load',()=>{const color=s=>getComputedStyle(document.querySelector(s)).backgroundColor;document.querySelector('#result').textContent=JSON.stringify({scheme:getComputedStyle(document.documentElement).colorScheme,body:color('body'),sidebar:color('.sidebar'),panel:color('.panel'),auth:color('.auth-card'),metric:color('.metric-card'),today:color('.today-stat'),setup:color('.template-card'),activity:color('.activity-card'),progress:color('.progress-activity'),ai:color('.ai-prompt-box'),activityDialog:color('.activity-create-dialog'),metricLabel:getComputedStyle(document.querySelector('.metric-card span')).color,calendar:color('.calendar-board'),input:color('input')})})</script></body></html>`;
        const file = join(temporary, 'theme.html');
        writeFileSync(file, fixture);
        const result = spawnSync(chrome, ['--headless=new', '--disable-gpu', '--no-sandbox', '--disable-extensions',
            `--user-data-dir=${join(temporary, 'profile')}`, '--window-size=1200,800', '--virtual-time-budget=1000',
            '--dump-dom', pathToFileURL(file).href], { encoding: 'utf8', timeout: 20000 });
        assert.equal(result.status, 0, result.stderr);
        const match = result.stdout.match(/<output id="result">([^<]+)<\/output>/);
        assert.ok(match, result.stdout);
        const colors = JSON.parse(match[1].replaceAll('&quot;', '"'));
        assert.equal(colors.scheme, 'dark');
        assert.equal(colors.body, 'rgb(11, 21, 35)');
        assert.equal(colors.sidebar, 'rgb(16, 29, 46)');
        for (const surface of ['panel', 'auth', 'metric', 'calendar', 'today', 'setup', 'activity', 'progress', 'activityDialog']) assert.equal(colors[surface], 'rgb(20, 36, 55)');
        assert.equal(colors.ai, 'rgb(28, 52, 71)');
        assert.equal(colors.metricLabel, 'rgb(174, 192, 201)');
        assert.equal(colors.input, 'rgb(16, 31, 48)');
    } finally {
        rmSync(temporary, { recursive: true, force: true });
    }
});

test('dark validation, setup and activity outcome states remain readable', { skip: !chrome }, () => {
    const temporary = mkdtempSync(join(tmpdir(), 'donysh-life-dark-states-'));
    try {
        const css = pathToFileURL(fileURLToPath(new URL('../wwwroot/css/app.css', import.meta.url))).href;
        const fixture = `<!doctype html><html lang="fa" dir="rtl" data-life-theme="dark"><head><link rel="stylesheet" href="${css}"></head><body><main class="panel"><div class="validation-summary">خطای فرم</div><p class="field-error">فیلد الزامی است</p><ol class="stepper"><li class="active">مرحلهٔ فعال</li><li class="complete">مرحلهٔ کامل</li></ol><button class="outcome-choice missed" aria-pressed="true">انجام‌نشده</button><a class="text-button danger">حذف فعالیت</a></main><output id="result"></output><script>addEventListener('load',()=>{const selectors=['.panel','.validation-summary','.field-error','.stepper .active','.stepper .complete','.outcome-choice.missed','.text-button.danger'];document.querySelector('#result').textContent=JSON.stringify(Object.fromEntries(selectors.map(selector=>{const style=getComputedStyle(document.querySelector(selector));return[selector,{color:style.color,background:style.backgroundColor}]})))})</script></body></html>`;
        const file = join(temporary, 'states.html');
        writeFileSync(file, fixture);
        const result = spawnSync(chrome, ['--headless=new', '--disable-gpu', '--no-sandbox', '--disable-extensions',
            `--user-data-dir=${join(temporary, 'profile')}`, '--virtual-time-budget=1000',
            '--dump-dom', pathToFileURL(file).href], { encoding: 'utf8', timeout: 20000 });
        assert.equal(result.status, 0, result.stderr);
        const match = result.stdout.match(/<output id="result">([^<]+)<\/output>/);
        assert.ok(match, result.stdout);
        const styles = JSON.parse(match[1].replaceAll('&quot;', '"'));
        const luminance = color => color.match(/[\d.]+/g).slice(0, 3).map(Number).map(value => {
            const channel = value / 255;
            return channel <= .04045 ? channel / 12.92 : ((channel + .055) / 1.055) ** 2.4;
        }).reduce((sum, channel, index) => sum + channel * [.2126, .7152, .0722][index], 0);
        const contrast = (foreground, background) => {
            const [bright, dark] = [luminance(foreground), luminance(background)].sort((a, b) => b - a);
            return (bright + .05) / (dark + .05);
        };
        for (const selector of ['.validation-summary', '.field-error', '.stepper .active', '.stepper .complete', '.outcome-choice.missed', '.text-button.danger']) {
            const ownBackground = styles[selector].background;
            const background = ownBackground === 'rgba(0, 0, 0, 0)' ? styles['.panel'].background : ownBackground;
            assert.ok(contrast(styles[selector].color, background) >= 4.5, `${selector} lacks dark-mode contrast`);
        }
    } finally {
        rmSync(temporary, { recursive: true, force: true });
    }
});

test('standalone error page follows the same dark theme as account pages', { skip: !chrome }, () => {
    const temporary = mkdtempSync(join(tmpdir(), 'donysh-life-error-theme-'));
    try {
        const css = pathToFileURL(fileURLToPath(new URL('../wwwroot/css/app.css', import.meta.url))).href;
        const script = pathToFileURL(fileURLToPath(new URL('../wwwroot/js/life-theme.js', import.meta.url))).href;
        const source = readFileSync(new URL('../Pages/Error.cshtml', import.meta.url), 'utf8');
        const fixture = source.replace(/^@page\s*\r?\n@\{[\s\S]*?\}\s*\r?\n/, '')
            .replace('~/js/life-theme.js', script).replace('~/css/app.css', css)
            .replace('</body>', '<output id="result"></output><script>addEventListener("load",()=>{const card=getComputedStyle(document.querySelector(".auth-card"));document.querySelector("#result").textContent=JSON.stringify({theme:document.documentElement.dataset.lifeTheme,card:card.backgroundColor,toggle:!!document.querySelector("[data-life-theme-toggle]")})})</script></body>');
        const file = join(temporary, 'error.html');
        writeFileSync(file, fixture);
        const result = spawnSync(chrome, ['--headless=new', '--force-dark-mode', '--disable-gpu', '--no-sandbox', '--disable-extensions',
            `--user-data-dir=${join(temporary, 'profile')}`, '--virtual-time-budget=1000',
            '--dump-dom', pathToFileURL(file).href], { encoding: 'utf8', timeout: 20000 });
        assert.equal(result.status, 0, result.stderr);
        const match = result.stdout.match(/<output id="result">([^<]+)<\/output>/);
        assert.ok(match, result.stdout);
        const actual = JSON.parse(match[1].replaceAll('&quot;', '"'));
        assert.equal(actual.theme, 'dark');
        assert.equal(actual.card, 'rgb(20, 36, 55)');
        assert.equal(actual.toggle, true);
    } finally {
        rmSync(temporary, { recursive: true, force: true });
    }
});
