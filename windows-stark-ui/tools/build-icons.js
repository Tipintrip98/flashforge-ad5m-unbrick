// Tipaldi AI - genera le icone (theme/icons/*.ico) e l'anteprima docs/img/icons-preview.png.
// Ogni icona: esagono HUD con contorno ciano/oro e simbolo luminoso nel colore del contesto.
// Uso: node tools/build-icons.js   (richiede Playwright con Chromium)
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const root = path.join(__dirname, '..');
const outDir = path.join(root, 'theme', 'icons');
const SIZES = [16, 24, 32, 48, 64, 128, 256];

const L = (d) => `<path d="${d}"/>`;
const C = (cx, cy, r) => `<circle cx="${cx}" cy="${cy}" r="${r}"/>`;
const F = (d) => `<path d="${d}" class="f"/>`;

// simboli su una griglia 256x256 (centro 128,128)
const ICONS = {
  video: { color: '#38D6FF', label: 'Montaggio video', g: [
    L('M78,96 h100 a10,10 0 0 1 10,10 v44 a10,10 0 0 1 -10,10 h-100 a10,10 0 0 1 -10,-10 v-44 a10,10 0 0 1 10,-10 z'),
    F('M116,110 L146,128 L116,146 Z'), L('M68,176 h120'), L('M84,168 v16 M108,168 v16 M132,168 v16 M156,168 v16')] },
  vfx: { color: '#B678FF', label: 'VFX e compositing', g: [
    L('M88,160 L128,96 L168,160 Z'), C(88, 160, 13), C(128, 96, 13), C(168, 160, 13), F('M128,140 l5,10 l11,2 l-8,8 l2,11 l-10,-5 l-10,5 l2,-11 l-8,-8 l11,-2 z')] },
  '3d': { color: '#FF8A3D', label: '3D', g: [
    L('M128,72 L178,100 L178,156 L128,184 L78,156 L78,100 Z'), L('M78,100 L128,128 L178,100'), L('M128,128 L128,184')] },
  print: { color: '#E8B04B', label: 'Stampa 3D', g: [
    L('M72,86 H184'), L('M116,86 V108 L128,122 L140,108 V86'), L('M92,176 H164'), L('M100,162 H156'), L('M108,148 H148'), C(128, 134, 3)] },
  design: { color: '#FF4F8B', label: 'Grafica', g: [
    L('M128,74 L164,138 L128,182 L92,138 Z'), C(128, 140, 8), L('M128,74 V132')] },
  audio: { color: '#2EC4B6', label: 'Audio', g: [
    L('M80,118 V138 M96,102 V154 M112,86 V170 M128,104 V152 M144,92 V164 M160,110 V146 M176,120 V136')] },
  dev: { color: '#4FE3A1', label: 'Sviluppo', g: [
    L('M106,94 L74,128 L106,162'), L('M150,94 L182,128 L150,162'), L('M138,82 L118,174')] },
  ai: { color: '#9AE6FF', label: 'AI', g: [
    F('M120,70 C126,110 136,120 176,126 C136,132 126,142 120,182 C114,142 104,132 64,126 C104,120 114,110 120,70 Z'),
    F('M172,72 C174,86 178,90 192,92 C178,94 174,98 172,112 C170,98 166,94 152,92 C166,90 170,86 172,72 Z')] },
  comm: { color: '#7FB2FF', label: 'Comunicazione', g: [
    L('M86,92 h84 a14,14 0 0 1 14,14 v42 a14,14 0 0 1 -14,14 h-44 l-24,20 v-20 h-16 a14,14 0 0 1 -14,-14 v-42 a14,14 0 0 1 14,-14 z'),
    C(104, 127, 5), C(128, 127, 5), C(152, 127, 5)] },
  office: { color: '#C9D6DF', label: 'Documenti e ufficio', g: [
    L('M96,72 h44 l28,28 v84 h-72 z'), L('M140,72 v28 h28'), L('M112,124 H152 M112,142 H152 M112,160 H140')] },
  fun: { color: '#FFD166', label: 'Svago', g: [C(128, 128, 52), F('M114,104 L154,128 L114,152 Z')] },
  system: { color: '#8899AA', label: 'Sistema', g: [
    C(128, 128, 26), L('M128,72 V92 M128,164 V184 M72,128 H92 M164,128 H184 M88,88 L102,102 M154,154 L168,168 M88,168 L102,154 M154,102 L168,88')] },
  web: { color: '#6E8494', label: 'Web', g: [C(128, 128, 54), L('M128,74 C100,100 100,156 128,182 C156,156 156,100 128,74'), L('M74,128 H182 M84,100 H172 M84,156 H172')] },
  altro: { color: '#56697A', label: 'Altro', g: [C(98, 128, 8), C(128, 128, 8), C(158, 128, 8)] },
  installer: { color: '#38D6FF', label: 'Installer', g: [
    L('M84,122 L128,144 L172,122 V168 L128,190 L84,168 Z'), L('M84,122 L128,100 L172,122'), L('M128,62 V112 M110,94 L128,112 L146,94')] },
  archive: { color: '#E8B04B', label: 'Archivi', g: [
    L('M80,96 H176 V176 H80 Z'), L('M80,116 H176'), L('M128,96 V116 M120,128 H136 V144 H120 Z')] },
  // icone di Windows (Questo PC, Cestino, Rete, Utente, cartella)
  thispc: { color: '#38D6FF', label: 'Questo PC', g: [
    L('M74,86 h108 a8,8 0 0 1 8,8 v62 a8,8 0 0 1 -8,8 h-108 a8,8 0 0 1 -8,-8 v-62 a8,8 0 0 1 8,-8 z'), L('M108,184 H148 M128,164 V184'), C(128, 125, 14)] },
  recycle: { color: '#38D6FF', label: 'Cestino vuoto', g: [
    L('M90,98 H166'), L('M114,98 V86 H142 V98'), L('M98,98 L106,182 H150 L158,98'), L('M118,116 V164 M138,116 V164')] },
  'recycle-full': { color: '#E8B04B', label: 'Cestino pieno', g: [
    L('M90,98 H166'), L('M98,98 L106,182 H150 L158,98'), F('M108,92 L124,64 L140,80 L156,62 L150,92 Z'), L('M118,116 V164 M138,116 V164')] },
  network: { color: '#38D6FF', label: 'Rete', g: [
    C(128, 84, 14), C(84, 164, 14), C(172, 164, 14), L('M128,98 V128 L84,150 M128,128 L172,150')] },
  user: { color: '#38D6FF', label: 'Utente', g: [C(128, 104, 26), L('M78,184 C82,146 104,134 128,134 C152,134 174,146 178,184')] },
  folder: { color: '#38D6FF', label: 'Cartella', g: [L('M74,96 h40 l12,14 h56 a8,8 0 0 1 8,8 v56 a8,8 0 0 1 -8,8 h-108 a8,8 0 0 1 -8,-8 v-70 a8,8 0 0 1 8,-8 z')] },
};

function svg(name, size) {
  const ic = ICONS[name];
  const small = size <= 24;
  const sw = small ? 16 : size <= 48 ? 11 : 8;           // tratti piu' spessi alle dimensioni piccole
  const hexW = small ? 14 : 6;
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 256 256">
  <defs>
    <linearGradient id="rim" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#38D6FF"/><stop offset="0.55" stop-color="#38D6FF" stop-opacity="0.65"/><stop offset="1" stop-color="#E8B04B"/></linearGradient>
    <radialGradient id="bg" cx="0.5" cy="0.42" r="0.62"><stop offset="0" stop-color="${ic.color}" stop-opacity="0.32"/><stop offset="0.7" stop-color="#0B1822" stop-opacity="0.96"/><stop offset="1" stop-color="#05080C"/></radialGradient>
    ${small ? '' : `<filter id="glow" x="-30%" y="-30%" width="160%" height="160%"><feGaussianBlur stdDeviation="5" result="b"/><feMerge><feMergeNode in="b"/><feMergeNode in="SourceGraphic"/></feMerge></filter>`}
  </defs>
  <path d="M128,8 L232,68 L232,188 L128,248 L24,188 L24,68 Z" fill="url(#bg)" stroke="url(#rim)" stroke-width="${hexW}" stroke-linejoin="round"/>
  ${small ? '' : '<path d="M128,26 L216,77 L216,179 L128,230 L40,179 L40,77 Z" fill="none" stroke="#38D6FF" stroke-opacity="0.18" stroke-width="2"/>'}
  <g fill="none" stroke="${ic.color}" stroke-width="${sw}" stroke-linecap="round" stroke-linejoin="round" ${small ? '' : 'filter="url(#glow)"'}>
    <style>.f{fill:${ic.color};stroke:none}</style>
    ${ic.g.join('\n    ')}
  </g>
</svg>`;
}

function ico(pngs) {
  const header = Buffer.alloc(6 + 16 * pngs.length);
  header.writeUInt16LE(0, 0); header.writeUInt16LE(1, 2); header.writeUInt16LE(pngs.length, 4);
  let offset = header.length;
  pngs.forEach((p, i) => {
    const e = 6 + 16 * i;
    header.writeUInt8(p.size >= 256 ? 0 : p.size, e);
    header.writeUInt8(p.size >= 256 ? 0 : p.size, e + 1);
    header.writeUInt8(0, e + 2); header.writeUInt8(0, e + 3);
    header.writeUInt16LE(1, e + 4); header.writeUInt16LE(32, e + 6);
    header.writeUInt32LE(p.data.length, e + 8); header.writeUInt32LE(offset, e + 12);
    offset += p.data.length;
  });
  return Buffer.concat([header, ...pngs.map((p) => p.data)]);
}

(async () => {
  fs.mkdirSync(outDir, { recursive: true });
  const browser = await chromium.launch(fs.existsSync('/opt/pw-browsers/chromium') ? { executablePath: '/opt/pw-browsers/chromium' } : {});
  const page = await browser.newPage();
  for (const name of Object.keys(ICONS)) {
    const pngs = [];
    for (const size of SIZES) {
      await page.setViewportSize({ width: size, height: size });
      await page.setContent(`<html><body style="margin:0;background:transparent">${svg(name, size)}</body></html>`);
      const data = await page.locator('svg').screenshot({ omitBackground: true });
      pngs.push({ size, data });
    }
    fs.writeFileSync(path.join(outDir, `${name}.ico`), ico(pngs));
  }
  // anteprima
  const cells = Object.keys(ICONS).map((n) => `<div class="c">${svg(n, 112)}<span>${ICONS[n].label}</span></div>`).join('');
  await page.setViewportSize({ width: 1200, height: 600 });
  await page.setContent(`<html><head><style>
    body{margin:0;background:radial-gradient(900px 400px at 50% 0,#0d2a3a,#05080c);font:13px 'Segoe UI',sans-serif;color:#9fb3c2}
    .g{display:grid;grid-template-columns:repeat(8,1fr);gap:18px 8px;padding:28px}.c{display:flex;flex-direction:column;align-items:center;gap:6px;text-align:center}
  </style></head><body><div class="g">${cells}</div></body></html>`);
  const h = await page.evaluate(() => document.body.scrollHeight);
  await page.setViewportSize({ width: 1200, height: h });
  await page.screenshot({ path: path.join(root, 'docs', 'img', 'icons-preview.png') });
  await browser.close();
  console.log(`${Object.keys(ICONS).length} icone in ${outDir}`);
})();
