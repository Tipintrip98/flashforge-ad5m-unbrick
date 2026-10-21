// Stark HUD - render degli sfondi statici dalla stessa scena dello sfondo animato Lively.
// Uso:  node tools/render-wallpapers.js   (dalla cartella windows-stark-ui)

const path = require('path');
const { pathToFileURL } = require('url');
const { chromium } = require('playwright');

const ROOT = path.resolve(__dirname, '..');
const PAGE = pathToFileURL(path.join(ROOT, 'components', 'lively', 'StarkReactor', 'index.html')).href;
const FONTS = pathToFileURL(path.join(ROOT, 'theme', 'fonts')).href + '/';

const JOBS = [
  { out: 'theme/wallpapers/StarkHUD-Desktop-4K.jpg', q: 'static=1&layout=desktop', w: 1920, h: 1080, dpr: 2 },
  { out: 'theme/wallpapers/StarkHUD-Lock-4K.jpg', q: 'static=1&layout=lock', w: 1920, h: 1080, dpr: 2 },
  { out: 'theme/wallpapers/StarkHUD-Desktop-Ultrawide.jpg', q: 'static=1&layout=desktop', w: 1720, h: 720, dpr: 2 },
  { out: 'components/lively/StarkReactor/thumbnail.jpg', q: 'static=1&layout=desktop', w: 1920, h: 1080, dpr: 1 / 3 },
  { out: 'docs/img/preview-desktop.jpg', q: 'static=1&layout=desktop', w: 1600, h: 900, dpr: 1 },
  { out: 'docs/img/preview-live.jpg', q: 'clock=1&demo=1', w: 1600, h: 900, dpr: 1, settle: 1500 },
];

(async () => {
  const browser = await chromium.launch();
  for (const job of JOBS) {
    const ctx = await browser.newContext({ viewport: { width: job.w, height: job.h }, deviceScaleFactor: job.dpr });
    const page = await ctx.newPage();
    await page.goto(`${PAGE}?${job.q}&fonts=${encodeURIComponent(FONTS)}`);
    await page.waitForSelector('body[data-ready="1"]');
    await page.waitForTimeout(job.settle || 400);
    await page.screenshot({ path: path.join(ROOT, job.out), type: 'jpeg', quality: 93 });
    console.log('ok', job.out);
    await ctx.close();
  }
  await browser.close();
})().catch((e) => { console.error(e); process.exit(1); });
