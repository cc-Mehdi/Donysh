import { copyFile, mkdir, readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';

const root = resolve(import.meta.dirname, '..');
const packageRoot = resolve(root, 'node_modules/lucide-static');
const destination = resolve(root, 'wwwroot/icons');
const names = [
  'activity', 'bike', 'book-open', 'briefcase', 'calendar-days', 'camera',
  'circle', 'code-xml', 'coffee', 'dumbbell', 'gamepad-2', 'graduation-cap',
  'heart', 'house', 'laptop', 'leaf', 'lightbulb', 'moon', 'music', 'palette',
  'pen-tool', 'plane', 'plus', 'log-out', 'panel-right-close', 'panel-right-open',
  'chart-no-axes-combined', 'sparkles', 'sun', 'target', 'users', 'utensils', 'wallet'
];

const symbols = await Promise.all(names.map(async (name) => {
  const source = await readFile(resolve(packageRoot, 'icons', `${name}.svg`), 'utf8');
  const match = source.match(/<svg\b[^>]*>([\s\S]*?)<\/svg>/);
  if (!match) throw new Error(`Invalid Lucide icon: ${name}`);
  return `<symbol id="${name}" viewBox="0 0 24 24">${match[1].trim()}</symbol>`;
}));

await mkdir(destination, { recursive: true });
await writeFile(resolve(destination, 'activities.svg'),
  `<svg xmlns="http://www.w3.org/2000/svg"><defs>${symbols.join('')}</defs></svg>\n`);
await copyFile(resolve(packageRoot, 'LICENSE'), resolve(destination, 'LUCIDE-LICENSE.txt'));
console.log(`Built local activity icon sprite with ${names.length} icons.`);
