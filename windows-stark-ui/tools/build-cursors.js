// Stark HUD - cursor builder
// Disegna i cursori come SVG vettoriali, li rasterizza con Chromium (Playwright)
// e li impacchetta in file Windows .cur / .ani multi-risoluzione (BMP 32 bit + alpha).
//
// Uso:  node tools/build-cursors.js            (dalla cartella windows-stark-ui)
// Output: theme/cursors/*.cur|*.ani  +  docs/img/cursors-preview.png

const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');

const ROOT = path.resolve(__dirname, '..');
const OUT = path.join(ROOT, 'theme', 'cursors');
const DOCS = path.join(ROOT, 'docs', 'img');

const C = {
  dark: '#0A0F14',
  line: '#E6F9FF',
  cyan: '#38D6FF',
  ice: '#7FE8FF',
  deep: '#0096C7',
  gold: '#F2B544',
  red: '#FF453A',
};

const STATIC_SIZES = [32, 48, 64, 96];
const ANIM_SIZES = [32, 48, 64];
const ANIM_FRAMES = 18;
const ANIM_JIFFIES = 3; // 1 jiffy = 1/60 s -> 18 frame x 50 ms = 0.9 s per giro

// ---------------------------------------------------------------- SVG helpers
const ARROW = 'M3,3 L3,25 L8.2,20.1 L11.9,28.4 L15.6,26.8 L11.9,18.7 L19,18.7 Z';

function svg(size, body) {
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 32 32">
  <defs>
    <filter id="glow" x="-50%" y="-50%" width="200%" height="200%"><feGaussianBlur stdDeviation="0.85"/></filter>
    <linearGradient id="cy" x1="0" y1="0" x2="0.4" y2="1">
      <stop offset="0" stop-color="${C.ice}"/><stop offset="1" stop-color="${C.deep}"/>
    </linearGradient>
    <radialGradient id="core" cx="0.5" cy="0.5" r="0.5">
      <stop offset="0" stop-color="#FFFFFF"/><stop offset="0.45" stop-color="${C.ice}"/>
      <stop offset="1" stop-color="${C.cyan}" stop-opacity="0"/>
    </radialGradient>
  </defs>
  ${body}
</svg>`;
}

// Forma piena con contorno chiaro e alone ciano
function shape(d, fill = C.dark, opts = {}) {
  const glow = opts.glow === false ? '' :
    `<path d="${d}" fill="none" stroke="${C.cyan}" stroke-width="2.4" stroke-linejoin="round" opacity="0.55" filter="url(#glow)"/>`;
  return `${glow}<path d="${d}" fill="${fill}" fill-opacity="${opts.fillOpacity ?? 0.94}" stroke="${C.line}" stroke-width="1.3" stroke-linejoin="round"/>`;
}

// Tratto sottile leggibile su ogni sfondo: base scura + linea chiara + alone
function stroke(d, opts = {}) {
  const w = opts.width ?? 1.4;
  return `<path d="${d}" fill="none" stroke="${C.cyan}" stroke-width="${w + 1.6}" stroke-linecap="round" opacity="0.5" filter="url(#glow)"/>
  <path d="${d}" fill="none" stroke="${C.dark}" stroke-width="${w + 2}" stroke-linecap="round" stroke-opacity="0.85"/>
  <path d="${d}" fill="none" stroke="${opts.color ?? C.line}" stroke-width="${w}" stroke-linecap="round"/>`;
}

const arrow = () => shape(ARROW) +
  `<path d="M5.2,9.2 L5.2,20.4" stroke="${C.cyan}" stroke-width="1" stroke-linecap="round"/>`;

const linkArrow = () => shape(ARROW, 'url(#cy)', { fillOpacity: 1 }) +
  `<path d="M5.2,9.2 L5.2,20.4" stroke="${C.dark}" stroke-width="1" stroke-linecap="round" opacity="0.8"/>`;

function spinner(cx, cy, r, t, opts = {}) {
  const circ = 2 * Math.PI * r;
  const arc = circ * 0.3;
  const inner = r * 0.58;
  const ic = 2 * Math.PI * inner;
  const seg = ic / 3;
  return `<circle cx="${cx}" cy="${cy}" r="${r + 1.6}" fill="${C.dark}" fill-opacity="0.6"/>
  <circle cx="${cx}" cy="${cy}" r="${r}" fill="none" stroke="${C.cyan}" stroke-opacity="0.22" stroke-width="${opts.w ?? 2}"/>
  <g transform="rotate(${(360 * t).toFixed(2)} ${cx} ${cy})">
    <circle cx="${cx}" cy="${cy}" r="${r}" fill="none" stroke="${C.cyan}" stroke-width="${(opts.w ?? 2) + 1.4}" stroke-linecap="round"
      stroke-dasharray="${arc.toFixed(2)} ${circ.toFixed(2)}" opacity="0.6" filter="url(#glow)"/>
    <circle cx="${cx}" cy="${cy}" r="${r}" fill="none" stroke="${C.ice}" stroke-width="${opts.w ?? 2}" stroke-linecap="round"
      stroke-dasharray="${arc.toFixed(2)} ${circ.toFixed(2)}"/>
  </g>
  ${opts.inner === false ? '' : `<g transform="rotate(${(-540 * t).toFixed(2)} ${cx} ${cy})">
    <circle cx="${cx}" cy="${cy}" r="${inner.toFixed(2)}" fill="none" stroke="${C.gold}" stroke-width="1.2"
      stroke-dasharray="${(seg * 0.68).toFixed(2)} ${(seg * 0.32).toFixed(2)}" opacity="0.9"/>
  </g>`}
  <circle cx="${cx}" cy="${cy}" r="${(r * 0.42).toFixed(2)}" fill="url(#core)" opacity="${(0.75 + 0.25 * Math.sin(t * Math.PI * 2)).toFixed(2)}"/>`;
}

function badge(cx, cy, r, content) {
  return `<circle cx="${cx}" cy="${cy}" r="${r + 1}" fill="${C.cyan}" opacity="0.5" filter="url(#glow)"/>
  <circle cx="${cx}" cy="${cy}" r="${r}" fill="url(#cy)" stroke="${C.line}" stroke-width="1.1"/>${content}`;
}

const DOUBLE_V = 'M16,3 L22,10 L18.4,10 L18.4,22 L22,22 L16,29 L10,22 L13.6,22 L13.6,10 L10,10 Z';
const FOUR_WAY = 'M16,2 L21,7.5 L18,7.5 L18,14 L24.5,14 L24.5,11 L30,16 L24.5,21 L24.5,18 L18,18 L18,24.5 L21,24.5 L16,30 L11,24.5 L14,24.5 L14,18 L7.5,18 L7.5,21 L2,16 L7.5,11 L7.5,14 L14,14 L14,7.5 L11,7.5 Z';

// ---------------------------------------------------------------- cursor set
// file, ruolo Windows (nome valore in HKCU\Control Panel\Cursors), hotspot su griglia 32
const CURSORS = [
  { file: 'arrow', role: 'Arrow', hs: [3, 3], draw: () => arrow() },
  { file: 'link', role: 'Hand', hs: [3, 3], draw: () => linkArrow() },
  { file: 'help', role: 'Help', hs: [3, 3], draw: () => arrow() + badge(25.2, 23.4, 5.6,
      `<path d="M23.2,21.9 C23.2,20.7 24.1,19.9 25.2,19.9 C26.3,19.9 27.1,20.6 27.1,21.6 C27.1,23 25.3,23.1 25.3,24.6" fill="none" stroke="${C.dark}" stroke-width="1.5" stroke-linecap="round"/>
       <circle cx="25.3" cy="26.6" r="0.95" fill="${C.dark}"/>`) },
  { file: 'text', role: 'IBeam', hs: [16, 16], draw: () => stroke(
      'M12,6 C14,6 15.2,6.8 16,8 C16.8,6.8 18,6 20,6 M16,8 L16,24 M12,26 C14,26 15.2,25.2 16,24 C16.8,25.2 18,26 20,26 M13.2,16 L18.8,16') },
  { file: 'precision', role: 'Crosshair', hs: [16, 16], draw: () =>
      stroke('M16,3 L16,11.5 M16,20.5 L16,29 M3,16 L11.5,16 M20.5,16 L29,16', { width: 1.3 }) +
      `<circle cx="16" cy="16" r="6" fill="none" stroke="${C.cyan}" stroke-width="1" stroke-dasharray="2.2 1.6" opacity="0.9"/>
       <circle cx="16" cy="16" r="1.4" fill="${C.gold}" stroke="${C.dark}" stroke-width="0.6"/>` },
  { file: 'unavailable', role: 'No', hs: [16, 16], draw: () =>
      `<circle cx="16" cy="16" r="9.6" fill="${C.dark}" fill-opacity="0.35" stroke="${C.dark}" stroke-width="5" stroke-opacity="0.8"/>
       <circle cx="16" cy="16" r="9.6" fill="none" stroke="${C.red}" stroke-width="2.6" opacity="0.6" filter="url(#glow)"/>
       <circle cx="16" cy="16" r="9.6" fill="none" stroke="${C.red}" stroke-width="2.4"/>
       <path d="M9.3,9.3 L22.7,22.7" stroke="${C.dark}" stroke-width="4.6" stroke-opacity="0.8" stroke-linecap="round"/>
       <path d="M9.3,9.3 L22.7,22.7" stroke="${C.red}" stroke-width="2.4" stroke-linecap="round"/>` },
  { file: 'move', role: 'SizeAll', hs: [16, 16], draw: () => shape(FOUR_WAY) +
      `<circle cx="16" cy="16" r="1.5" fill="${C.cyan}"/>` },
  { file: 'vert', role: 'SizeNS', hs: [16, 16], draw: () => shape(DOUBLE_V) },
  { file: 'horz', role: 'SizeWE', hs: [16, 16], draw: () => `<g transform="rotate(90 16 16)">${shape(DOUBLE_V)}</g>` },
  { file: 'dgn1', role: 'SizeNWSE', hs: [16, 16], draw: () => `<g transform="rotate(-45 16 16) translate(16 16) scale(0.92) translate(-16 -16)">${shape(DOUBLE_V)}</g>` },
  { file: 'dgn2', role: 'SizeNESW', hs: [16, 16], draw: () => `<g transform="rotate(45 16 16) translate(16 16) scale(0.92) translate(-16 -16)">${shape(DOUBLE_V)}</g>` },
  { file: 'alternate', role: 'UpArrow', hs: [16, 3], draw: () => shape('M16,3 L25,13 L19.2,13 L19.2,29 L12.8,29 L12.8,13 L7,13 Z') +
      `<path d="M16,16 L16,26" stroke="${C.cyan}" stroke-width="1" stroke-linecap="round"/>` },
  { file: 'handwriting', role: 'NWPen', hs: [4, 28], draw: () => shape('M4,28 L6,21 L22,5 L27,10 L11,26 Z') +
      `<path d="M6,21 L11,26" stroke="${C.line}" stroke-width="1.1"/>
       <path d="M19.6,7.4 L24.6,12.4" stroke="${C.gold}" stroke-width="1.6"/>
       <path d="M8.6,20.4 L19.4,9.6" stroke="${C.cyan}" stroke-width="1" stroke-linecap="round"/>` },
  { file: 'pin', role: 'Pin', hs: [3, 3], draw: () => arrow() +
      `<path d="M25.4,30 C25.4,30 20.8,24.6 20.8,21.6 A4.6,4.6 0 1 1 30,21.6 C30,24.6 25.4,30 25.4,30 Z" fill="${C.cyan}" opacity="0.5" filter="url(#glow)"/>
       <path d="M25.4,30 C25.4,30 20.8,24.6 20.8,21.6 A4.6,4.6 0 1 1 30,21.6 C30,24.6 25.4,30 25.4,30 Z" fill="url(#cy)" stroke="${C.line}" stroke-width="1.1"/>
       <circle cx="25.4" cy="21.6" r="1.7" fill="${C.dark}"/>` },
  { file: 'person', role: 'Person', hs: [3, 3], draw: () => arrow() + badge(25.2, 23.6, 5.8,
      `<circle cx="25.2" cy="21.7" r="1.9" fill="${C.dark}"/>
       <path d="M21.9,27.4 C22.3,25.2 23.6,24.3 25.2,24.3 C26.8,24.3 28.1,25.2 28.5,27.4 Z" fill="${C.dark}"/>`) },
];

const ANIMATED = [
  { file: 'busy', role: 'Wait', hs: [16, 16], draw: (t) => spinner(16, 16, 11, t) },
  { file: 'working', role: 'AppStarting', hs: [3, 3], draw: (t) => arrow() + spinner(25, 24.5, 5, t, { w: 1.6, inner: false }) },
];

// ---------------------------------------------------------------- binary packing
function dibEntry(rgba, size) {
  const header = Buffer.alloc(40);
  const rowAnd = Math.ceil(size / 32) * 4;
  const xorSize = size * size * 4;
  const andSize = rowAnd * size;
  header.writeUInt32LE(40, 0);
  header.writeInt32LE(size, 4);
  header.writeInt32LE(size * 2, 8); // XOR + AND
  header.writeUInt16LE(1, 12);
  header.writeUInt16LE(32, 14);
  header.writeUInt32LE(0, 16);
  header.writeUInt32LE(xorSize + andSize, 20);
  const xor = Buffer.alloc(xorSize);
  const and = Buffer.alloc(andSize);
  for (let y = 0; y < size; y++) {
    const dy = size - 1 - y; // bottom-up
    for (let x = 0; x < size; x++) {
      const s = (y * size + x) * 4;
      const d = (dy * size + x) * 4;
      xor[d] = rgba[s + 2];
      xor[d + 1] = rgba[s + 1];
      xor[d + 2] = rgba[s];
      xor[d + 3] = rgba[s + 3];
      if (rgba[s + 3] === 0) and[dy * rowAnd + (x >> 3)] |= 0x80 >> (x & 7);
    }
  }
  return Buffer.concat([header, xor, and]);
}

function packCur(images, hs) {
  // images: [{size, rgba}]
  const dir = Buffer.alloc(6 + 16 * images.length);
  dir.writeUInt16LE(0, 0);
  dir.writeUInt16LE(2, 2); // 2 = cursor
  dir.writeUInt16LE(images.length, 4);
  const blobs = images.map((im) => dibEntry(im.rgba, im.size));
  let offset = dir.length;
  images.forEach((im, i) => {
    const e = 6 + 16 * i;
    dir.writeUInt8(im.size >= 256 ? 0 : im.size, e);
    dir.writeUInt8(im.size >= 256 ? 0 : im.size, e + 1);
    dir.writeUInt8(0, e + 2);
    dir.writeUInt8(0, e + 3);
    dir.writeUInt16LE(Math.round((hs[0] * im.size) / 32), e + 4);
    dir.writeUInt16LE(Math.round((hs[1] * im.size) / 32), e + 6);
    dir.writeUInt32LE(blobs[i].length, e + 8);
    dir.writeUInt32LE(offset, e + 12);
    offset += blobs[i].length;
  });
  return Buffer.concat([dir, ...blobs]);
}

function chunk(id, data) {
  const head = Buffer.alloc(8);
  head.write(id, 0, 'ascii');
  head.writeUInt32LE(data.length, 4);
  const pad = data.length % 2 ? Buffer.alloc(1) : Buffer.alloc(0);
  return Buffer.concat([head, data, pad]);
}

function list(type, chunks) {
  return chunk('LIST', Buffer.concat([Buffer.from(type, 'ascii'), ...chunks]));
}

function packAni(frames, title) {
  const anih = Buffer.alloc(36);
  anih.writeUInt32LE(36, 0);
  anih.writeUInt32LE(frames.length, 4); // nFrames
  anih.writeUInt32LE(frames.length, 8); // nSteps
  anih.writeUInt32LE(ANIM_JIFFIES, 28); // iDispRate
  anih.writeUInt32LE(1, 32); // AF_ICON
  const info = list('INFO', [
    chunk('INAM', Buffer.from(title + '\0', 'ascii')),
    chunk('IART', Buffer.from('Stark HUD UI Pack\0', 'ascii')),
  ]);
  const fram = list('fram', frames.map((f) => chunk('icon', f)));
  const body = Buffer.concat([Buffer.from('ACON', 'ascii'), info, chunk('anih', anih), fram]);
  const head = Buffer.alloc(8);
  head.write('RIFF', 0, 'ascii');
  head.writeUInt32LE(body.length, 4);
  return Buffer.concat([head, body]);
}

// ---------------------------------------------------------------- render
async function rasterize(page, markup, size) {
  return Buffer.from(await page.evaluate(async ({ markup, size }) => {
    const img = new Image();
    img.src = 'data:image/svg+xml;base64,' + btoa(markup);
    await img.decode();
    const c = document.createElement('canvas');
    c.width = c.height = size;
    const ctx = c.getContext('2d');
    ctx.drawImage(img, 0, 0, size, size);
    return Array.from(ctx.getImageData(0, 0, size, size).data);
  }, { markup, size }));
}

async function main() {
  fs.mkdirSync(OUT, { recursive: true });
  fs.mkdirSync(DOCS, { recursive: true });
  const browser = await chromium.launch();
  const page = await browser.newPage();
  await page.setContent('<html><body></body></html>');

  const preview = [];
  for (const cur of CURSORS) {
    const images = [];
    for (const size of STATIC_SIZES) images.push({ size, rgba: await rasterize(page, svg(size, cur.draw()), size) });
    fs.writeFileSync(path.join(OUT, cur.file + '.cur'), packCur(images, cur.hs));
    preview.push({ name: cur.role, markup: svg(64, cur.draw()) });
    console.log('cur', cur.file);
  }
  for (const ani of ANIMATED) {
    const frames = [];
    for (let i = 0; i < ANIM_FRAMES; i++) {
      const t = i / ANIM_FRAMES;
      const images = [];
      for (const size of ANIM_SIZES) images.push({ size, rgba: await rasterize(page, svg(size, ani.draw(t)), size) });
      frames.push(packCur(images, ani.hs));
    }
    fs.writeFileSync(path.join(OUT, ani.file + '.ani'), packAni(frames, 'Stark HUD ' + ani.role));
    preview.push({ name: ani.role, markup: svg(64, ani.draw(0.15)) });
    console.log('ani', ani.file);
  }

  // Foglio anteprima per la documentazione (sfondo scuro e chiaro)
  const cells = preview.map((p) => `<figure><div class="d">${p.markup}</div><div class="l">${p.markup}</div><figcaption>${p.name}</figcaption></figure>`).join('');
  await page.setViewportSize({ width: 1180, height: 420 });
  await page.setContent(`<html><head><style>
    body{margin:0;background:#070B10;font:12px/1.2 'DejaVu Sans Mono',monospace;color:#8FA6B8;padding:22px}
    .g{display:grid;grid-template-columns:repeat(9,1fr);gap:14px}
    figure{margin:0;text-align:center}
    .d,.l{height:76px;display:flex;align-items:center;justify-content:center;border-radius:14px}
    .d{background:#0E151C;border:1px solid #16303C}.l{background:#E9EEF2;margin-top:6px}
    figcaption{margin-top:6px;letter-spacing:.06em}
  </style></head><body><div class="g">${cells}</div></body></html>`);
  await page.screenshot({ path: path.join(DOCS, 'cursors-preview.png'), fullPage: true });
  await browser.close();
}

main().catch((e) => { console.error(e); process.exit(1); });
