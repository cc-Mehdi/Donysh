import { copyFile, mkdir, readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';

const root = resolve(import.meta.dirname, '..');
const packageRoot = resolve(root, 'node_modules/lucide-static');
const destination = resolve(root, 'wwwroot/icons');
const names = [
  'arrow-left', 'banknote', 'bell', 'calendar-days', 'chart-column-increasing',
  'chart-no-axes-combined', 'chart-pie', 'chevron-down', 'circle-alert',
  'circle-check', 'circle-dollar-sign', 'clock-3', 'credit-card', 'house',
  'landmark', 'leaf', 'lightbulb', 'log-out', 'menu', 'moon', 'panel-right-close',
  'panel-right-open', 'piggy-bank', 'plus',
  'receipt', 'repeat-2', 'search', 'settings', 'smartphone', 'sparkles',
  'sun', 'tags', 'target', 'trending-down', 'trending-up', 'wallet', 'x'
];

const symbols = await Promise.all(names.map(async (name) => {
  const source = await readFile(resolve(packageRoot, 'icons', `${name}.svg`), 'utf8');
  const match = source.match(/<svg\b[^>]*>([\s\S]*?)<\/svg>/);
  if (!match) throw new Error(`Invalid Lucide icon: ${name}`);
  return `<symbol id="${name}" viewBox="0 0 24 24">${match[1].trim()}</symbol>`;
}));

await mkdir(destination, { recursive: true });
await writeFile(resolve(destination, 'lucide.svg'),
  `<svg xmlns="http://www.w3.org/2000/svg"><defs>${symbols.join('')}</defs></svg>\n`);
await copyFile(resolve(packageRoot, 'LICENSE'), resolve(destination, 'LUCIDE-LICENSE.txt'));
console.log(`Built local Lucide sprite with ${names.length} icons.`);
