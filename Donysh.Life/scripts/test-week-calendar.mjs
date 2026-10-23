import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { existsSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { test } from 'node:test';

const chrome = [
  process.env.CHROME_BIN,
  'C:/Program Files/Google/Chrome/Application/chrome.exe',
  'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  '/usr/bin/google-chrome',
  '/usr/bin/chromium'
].find((path) => path && existsSync(path));

const css = pathToFileURL(fileURLToPath(new URL('../wwwroot/css/app.css', import.meta.url))).href;
const days = ['شنبه', 'یکشنبه', 'دوشنبه', 'سه‌شنبه', 'چهارشنبه', 'پنجشنبه', 'جمعه'];
const header = days.map((name, index) => `<a class="${index === 2 ? 'today' : ''}"><strong>${name}</strong><span>${index + 1}</span><small>${index === 2 ? 'امروز · ' : ''}۰ برنامه</small></a>`).join('');
const columns = days.map(() => '<article class="calendar-day-column"></article>').join('');
const times = Array.from({ length: 24 }, (_, hour) => `<span>${String(hour).padStart(2, '0')}:00</span>`).join('');
const fixture = `<!doctype html><html lang="fa" dir="rtl"><head><meta charset="utf-8"><link rel="stylesheet" href="${css}"></head><body style="padding:12px">
<section class="calendar-board week-view"><div class="calendar-corner"></div><div class="calendar-days-header">${header}</div><div class="calendar-time-gutter">${times}</div><div class="calendar-days-grid">${columns}</div></section>
<output id="result"></output><script>addEventListener('load', () => {
  const board = document.querySelector('.calendar-board'), gutter = document.querySelector('.calendar-time-gutter'), corner = document.querySelector('.calendar-corner');
  const position = (element) => { const rect = element.getBoundingClientRect(), viewport = board.getBoundingClientRect(); return { left: rect.left - viewport.left, right: viewport.right - rect.right, width: rect.width }; };
  const before = { gutter: position(gutter), corner: position(corner) };
  const firstDay = document.querySelector('.calendar-days-header a:first-child');
  const today = document.querySelector('.calendar-days-header a.today');
  board.scrollLeft = -240;
  const after = { gutter: position(gutter), corner: position(corner) };
  document.querySelector('#result').textContent = JSON.stringify({ width: innerWidth, boardWidth: board.clientWidth, scrollWidth: board.scrollWidth, scrollLeft: board.scrollLeft, before, after,
    firstDayBackground: getComputedStyle(firstDay).backgroundColor, todayBackground: getComputedStyle(today).backgroundColor });
});</script></body></html>`;

function render(width) {
  const temporary = mkdtempSync(join(tmpdir(), 'donysh-week-calendar-'));
  try {
    const page = join(temporary, 'calendar.html');
    writeFileSync(page, fixture);
    const result = spawnSync(chrome, ['--headless=new', '--disable-gpu', '--no-sandbox', '--disable-extensions',
      `--user-data-dir=${join(temporary, 'profile')}`, `--window-size=${width},720`, '--virtual-time-budget=1000',
      '--dump-dom', pathToFileURL(page).href], { encoding: 'utf8', timeout: 20000 });
    assert.equal(result.status, 0, result.stderr);
    const match = result.stdout.match(/<output id="result">([^<]+)<\/output>/);
    assert.ok(match, result.stdout);
    return JSON.parse(match[1].replaceAll('&quot;', '"'));
  } finally {
    rmSync(temporary, { recursive: true, force: true });
  }
}

test('week calendar keeps the full hour column visible while scrolling horizontally', { skip: !chrome }, () => {
  for (const width of [500, 800]) {
    const layout = render(width);
    assert.ok(layout.scrollWidth > layout.boardWidth);
    for (const state of [layout.before, layout.after]) {
      for (const part of [state.gutter, state.corner]) {
        assert.ok(part.left >= -1, JSON.stringify(layout));
        assert.ok(part.right >= -1, JSON.stringify(layout));
        assert.ok(part.width >= 60, JSON.stringify(layout));
      }
    }
  }
});

test('only the actual date receives the today highlight', { skip: !chrome }, () => {
  const layout = render(800);
  assert.notEqual(layout.todayBackground, layout.firstDayBackground);
});

test('overlapping cards remain separate and dragging previews the proposed time before dropping', { skip: !chrome }, () => {
  const temporary = mkdtempSync(join(tmpdir(), 'donysh-week-drag-'));
  try {
    const script = pathToFileURL(fileURLToPath(new URL('../wwwroot/js/week-planner.js', import.meta.url))).href;
    const event = (title, start, duration, visible) => `<div class="calendar-slot" data-minute="${start}"><button class="time-slot"></button><article class="calendar-event" draggable="true" data-pattern-id="${title}" data-original-date="2026-10-24" data-scheduled-date="2026-10-24" data-start-minute="${start}" data-duration="${duration}" data-display-start-minute="${start}" data-display-duration="${visible}"><div class="block-main"><div><strong>${title}</strong><span class="block-time"></span></div></div></article></div>`;
    const fixture = `<!doctype html><html lang="fa" dir="rtl"><head><link rel="stylesheet" href="${css}"></head><body><section class="calendar-board" data-calendar-board><div class="calendar-day-column" data-date="2026-10-24">${event('خواب', 1320, 480, 120)}${event('مطالعه', 1350, 60, 60)}<div class="calendar-slot" data-minute="1380"><button class="time-slot"></button></div></div></section><dialog id="schedule-create-dialog"><form class="schedule-form"><input name="Input.StartTime" value="22:00"><input name="Input.EndTime" value="06:00"><input name="Input.DurationMinutes" value="60"><p data-end-day-hint></p></form></dialog><dialog id="schedule-scope-dialog"></dialog><form id="schedule-interaction-form"></form><output id="result"></output><script src="${script}"></script><script>addEventListener('load',()=>{const events=[...document.querySelectorAll('.calendar-event')],target=document.querySelector('[data-minute="1380"]'),transfer=new DataTransfer();events[0].dispatchEvent(new DragEvent('dragstart',{bubbles:true,cancelable:true,dataTransfer:transfer}));target.dispatchEvent(new DragEvent('dragover',{bubbles:true,cancelable:true,dataTransfer:transfer}));const preview=target.querySelector('.calendar-drag-preview');document.querySelector('#result').textContent=JSON.stringify({columns:events.map(e=>e.style.getPropertyValue('--overlap-width')),positions:events.map(e=>({x:e.getBoundingClientRect().x,width:e.getBoundingClientRect().width})),dayWidth:document.querySelector('.calendar-day-column').getBoundingClientRect().width,preview:preview?.textContent,duration:document.querySelector('[name="Input.DurationMinutes"]').value,hint:document.querySelector('[data-end-day-hint]').textContent})})</script></body></html>`;
    const file = join(temporary, 'drag.html');
    writeFileSync(file, fixture);
    const result = spawnSync(chrome, ['--headless=new', '--disable-gpu', '--no-sandbox', '--disable-extensions',
      `--user-data-dir=${join(temporary, 'profile')}`, '--virtual-time-budget=1000', '--dump-dom', pathToFileURL(file).href],
    { encoding: 'utf8', timeout: 20000 });
    assert.equal(result.status, 0, result.stderr);
    const match = result.stdout.match(/<output id="result">([^<]+)<\/output>/);
    assert.ok(match, result.stdout);
    const actual = JSON.parse(match[1].replaceAll('&quot;', '"'));
    assert.deepEqual(actual.columns, ['50%', '50%']);
    assert.ok(actual.positions.every(item => item.width < actual.dayWidth * .55));
    assert.notEqual(actual.positions[0].x, actual.positions[1].x);
    assert.match(actual.preview, /23:00 تا 07:00 فردا/);
    assert.equal(actual.duration, '480');
    assert.match(actual.hint, /روز بعد/);
  } finally {
    rmSync(temporary, { recursive: true, force: true });
  }
});
