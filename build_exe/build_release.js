const fs = require('fs');
const path = require('path');
const vm = require('vm');
const { execFileSync } = require('child_process');

const projectRoot = path.resolve(__dirname, '..');
const buildDir = __dirname;
const aeroDir = path.join(projectRoot, 'J80_AeroHUD');
const releasesDir = path.join(projectRoot, 'releases');
const versionJsonPath = path.join(buildDir, 'version.json');
const srcHtmlPath = path.join(aeroDir, 'index.html');
const threeJsPath = path.join(aeroDir, 'three.min.js');
const iconPath = path.join(buildDir, 'app_icon.ico');
const programCsPath = path.join(buildDir, 'Program.cs');
const videoPath = path.join(aeroDir, 'water_bg.mp4');
const gifPath = path.join(aeroDir, 'water_bg.gif');
const jpgPath = path.join(aeroDir, 'water_bg.jpg');

// ---------------------------------------------------------------------------
// 1. Version Resolution (+0.1 Auto-Increment per Build by Default)
// ---------------------------------------------------------------------------
const args = process.argv.slice(2);
const noBump = args.includes('--no-bump');
const setIdx = args.indexOf('--set');
const explicitSet = setIdx >= 0 && args[setIdx + 1] ? args[setIdx + 1] : null;

const verCfg = JSON.parse(fs.readFileSync(versionJsonPath, 'utf8'));
let nextVer;
if (explicitSet) {
  nextVer = parseFloat(explicitSet).toFixed(1);
} else if (noBump) {
  nextVer = parseFloat(verCfg.version).toFixed(1);
} else {
  const curTenths = Math.round(parseFloat(verCfg.version) * 10);
  const stepTenths = Math.round((verCfg.step || 0.1) * 10);
  nextVer = ((curTenths + stepTenths) / 10).toFixed(1);
}

const [majorPart, minorPart] = nextVer.split('.');
const asmVersion = `${majorPart}.${minorPart}.0.0`;
const verTag = `v${nextVer}`;

console.log(`[1/6] Building release ${verTag} (previous: v${verCfg.version}, assembly: ${asmVersion})...`);

// ---------------------------------------------------------------------------
// 2. Sync Version Strings in Source index.html & Program.cs
// ---------------------------------------------------------------------------
let srcHtml = fs.readFileSync(srcHtmlPath, 'utf8').replace(/\r\n/g, '\n');

const titleRegex = /<title>서울수상레포츠센터 요트교육 v[0-9.]+<\/title>/;
if (!titleRegex.test(srcHtml)) {
  throw new Error('<title> version tag not found in J80_AeroHUD/index.html');
}
srcHtml = srcHtml.replace(titleRegex, `<title>서울수상레포츠센터 요트교육 ${verTag}</title>`);

const badgeRegex = /(<span id="appVersionBadge"[^>]*>)v[0-9.]+(<\/span>)/;
if (!badgeRegex.test(srcHtml)) {
  throw new Error('#appVersionBadge span not found in J80_AeroHUD/index.html');
}
srcHtml = srcHtml.replace(badgeRegex, `$1${verTag}$2`);

fs.writeFileSync(srcHtmlPath, srcHtml, 'utf8');
fs.writeFileSync(path.join(buildDir, 'index.html'), srcHtml, 'utf8');
fs.copyFileSync(threeJsPath, path.join(buildDir, 'three.min.js'));

let programCs = fs.readFileSync(programCsPath, 'utf8').replace(/^\uFEFF/, '');
programCs = programCs
  .replace(/\[assembly: AssemblyTitle\("[^"]*"\)\]/, `[assembly: AssemblyTitle("서울수상레포츠센터 요트교육 시뮬레이터 ${verTag}")]`)
  .replace(/\[assembly: AssemblyDescription\("[^"]*"\)\]/, `[assembly: AssemblyDescription("Keel boat & ILCA 7 딩기요트 통합 공기역학 시뮬레이터 (Ver ${nextVer} - Spatial Glass & Luxury Marine Chronometer)")]`)
  .replace(/\[assembly: AssemblyVersion\("[^"]*"\)\]/, `[assembly: AssemblyVersion("${asmVersion}")]`)
  .replace(/\[assembly: AssemblyFileVersion\("[^"]*"\)\]/, `[assembly: AssemblyFileVersion("${asmVersion}")]`);
fs.writeFileSync(programCsPath, '\uFEFF' + programCs, 'utf8');

// ---------------------------------------------------------------------------
// 3. Static Verification: DOM IDs, Canvas save/restore Balance & JS Syntax
// ---------------------------------------------------------------------------
const htmlIds = new Set([...srcHtml.matchAll(/\bid="([^"]+)"/g)].map(m => m[1]));
const referencedIds = new Set([...srcHtml.matchAll(/getElementById\('([^']+)'\)/g)].map(m => m[1]));
for (const refId of referencedIds) {
  if (!htmlIds.has(refId)) {
    throw new Error(`Missing DOM ID in HTML: ${refId}`);
  }
}

function extractFunctionBody(src, startSig) {
  const startIdx = src.indexOf(startSig);
  if (startIdx < 0) throw new Error('Function not found: ' + startSig);
  const parenStart = src.indexOf('(', startIdx);
  let pDepth = 0;
  let paramEnd = parenStart;
  for (let i = parenStart; i < src.length; i++) {
    if (src[i] === '(') pDepth++;
    else if (src[i] === ')') {
      pDepth--;
      if (pDepth === 0) { paramEnd = i; break; }
    }
  }
  const braceStart = src.indexOf('{', paramEnd);
  let depth = 0;
  for (let i = braceStart; i < src.length; i++) {
    if (src[i] === '{') depth++;
    else if (src[i] === '}') {
      depth--;
      if (depth === 0) return src.slice(braceStart, i + 1);
    }
  }
  throw new Error('Unclosed function: ' + startSig);
}

const canvasFns = [
  'function computeTelltaleState',
  'function drawTelltaleRibbon',
  'function draw2DYacht',
  'function drawVectorArrow',
  'function drawSailingCompass',
  'function renderMainStage',
  'function renderHeelCrossSection',
  'function renderForceHistoryGraph'
];
for (const sig of canvasFns) {
  const body = extractFunctionBody(srcHtml, sig);
  const saves = (body.match(/\.save\(\)/g) || []).length;
  const restores = (body.match(/\.restore\(\)/g) || []).length;
  if (saves !== restores) {
    throw new Error(`Unbalanced ctx.save/restore in ${sig}: save=${saves}, restore=${restores}`);
  }
}
console.log(`[2/6] Verified ${referencedIds.size} referenced DOM IDs and balanced save()/restore() across ${canvasFns.length} functions.`);

// ---------------------------------------------------------------------------
// 4. Build Self-Contained PC Web Server HTML & ZIP
// ---------------------------------------------------------------------------
const threeJs = fs.readFileSync(threeJsPath, 'utf8');
const iconBase64 = fs.readFileSync(iconPath).toString('base64');

let webHtml = srcHtml;
if (!webHtml.includes('<link rel="icon" type="image/x-icon" href="/app_icon.ico" />')) {
  throw new Error('Favicon tag not found');
}
webHtml = webHtml.replace(
  '<link rel="icon" type="image/x-icon" href="/app_icon.ico" />',
  `<link rel="icon" type="image/x-icon" href="data:image/x-icon;base64,${iconBase64}" />`
);

if (!webHtml.includes('<script src="three.min.js"></script>')) {
  throw new Error('three.min.js script tag not found');
}
webHtml = webHtml.replace(
  '<script src="three.min.js"></script>',
  `<script>/* Embedded Three.js for Web Server Deployment */\n${threeJs}\n</script>`
);

const heartbeatTarget = `    try {
      if (location.protocol.indexOf('http') === 0) {
        setInterval(() => {
          fetch('/api/heartbeat').catch(() => {});
        }, 2500);
        window.addEventListener('beforeunload', () => {
          navigator.sendBeacon('/api/exit');
        });
      }
    } catch (err) {}`;
if (!webHtml.includes(heartbeatTarget)) {
  throw new Error('Heartbeat block not found');
}
webHtml = webHtml.replace(heartbeatTarget, `    // Web Server HTML Version (${verTag}) - No local EXE heartbeat polling required`);

const scripts = [...webHtml.matchAll(/<script>([\s\S]*?)<\/script>/g)];
scripts.forEach((m, idx) => {
  new vm.Script(m[1], { filename: `web_script_${idx}.js` });
});

const baseName = `서울수상레포츠센터_요트교육_${verTag}`;
fs.mkdirSync(releasesDir, { recursive: true });
const webDir = path.join(releasesDir, `${baseName}_PC웹서버용`);
const webStandaloneHtml = path.join(releasesDir, `${baseName}_PC웹서버용.html`);
const mobileStandaloneHtml = path.join(releasesDir, `${baseName}_모바일용.html`);
const webZipPath = path.join(releasesDir, `${baseName}_PC웹서버용.zip`);

fs.mkdirSync(webDir, { recursive: true });
fs.writeFileSync(webStandaloneHtml, webHtml, 'utf8');
fs.writeFileSync(mobileStandaloneHtml, webHtml, 'utf8');
fs.writeFileSync(path.join(webDir, 'index.html'), webHtml, 'utf8');
fs.writeFileSync(path.join(webDir, `${baseName}_PC웹서버용.html`), webHtml, 'utf8');
console.log(`[3/6] Built Web Server & Mobile HTML:\n  - ${webStandaloneHtml} (${(fs.statSync(webStandaloneHtml).size / 1024).toFixed(1)} KB)\n  - ${mobileStandaloneHtml} (${(fs.statSync(mobileStandaloneHtml).size / 1024).toFixed(1)} KB)`);

// ---------------------------------------------------------------------------
// 5. Compile Standalone Windows .exe via csc.exe
// ---------------------------------------------------------------------------
const cscCandidates = [
  'C:\\Windows\\Microsoft.NET\\Framework64\\v4.0.30319\\csc.exe',
  'C:\\Windows\\Microsoft.NET\\Framework\\v4.0.30319\\csc.exe'
];
const cscExe = cscCandidates.find(p => fs.existsSync(p));
if (!cscExe) {
  throw new Error('csc.exe not found in .NET Framework v4.0.30319');
}

const exeName = `${baseName}.exe`;
const exeInBuild = path.join(buildDir, exeName);
const exeInReleases = path.join(releasesDir, exeName);
const exePkgDir = path.join(releasesDir, `${baseName}_단일실행파일`);
const exeZipPath = path.join(releasesDir, `${baseName}_단일실행파일.zip`);

const rspPath = path.join(buildDir, 'compile_release.rsp');
const rspLines = [
  '/nologo',
  '/target:winexe',
  '/optimize+',
  '/utf8output',
  `/win32icon:"${iconPath}"`,
  `/out:"${exeInBuild}"`,
  `/resource:"${path.join(buildDir, 'index.html')}",index.html`,
  `/resource:"${path.join(buildDir, 'three.min.js')}",three.min.js`,
  `/resource:"${iconPath}",app_icon.ico`,
  '/reference:System.dll',
  '/reference:System.Windows.Forms.dll',
  `"${programCsPath}"`
];
fs.writeFileSync(rspPath, '\uFEFF' + rspLines.join('\r\n'), 'utf8');

execFileSync(cscExe, [`@${rspPath}`], { stdio: 'pipe' });
fs.unlinkSync(rspPath);

fs.mkdirSync(exePkgDir, { recursive: true });
fs.copyFileSync(exeInBuild, exeInReleases);
fs.copyFileSync(exeInBuild, path.join(exePkgDir, exeName));
console.log(`[4/6] Compiled Standalone EXE: ${exeInReleases} (${(fs.statSync(exeInReleases).size / 1024).toFixed(1)} KB)`);

// ---------------------------------------------------------------------------
// 6. Create Unicode-Safe .zip Archives via .NET ZipFile (-EncodedCommand)
// ---------------------------------------------------------------------------
function createZipFromDir(sourceDir, destZip) {
  if (fs.existsSync(destZip)) fs.unlinkSync(destZip);
  const psScript = `
    $ErrorActionPreference = 'Stop'
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory('${sourceDir.replace(/'/g, "''")}', '${destZip.replace(/'/g, "''")}')
  `;
  const encoded = Buffer.from(psScript, 'utf16le').toString('base64');
  execFileSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-EncodedCommand', encoded], { stdio: 'pipe' });
}

createZipFromDir(webDir, webZipPath);
createZipFromDir(exePkgDir, exeZipPath);
console.log(`[5/6] Created ZIP archives:\n  - ${webZipPath} (${(fs.statSync(webZipPath).size / 1024).toFixed(1)} KB)\n  - ${exeZipPath} (${(fs.statSync(exeZipPath).size / 1024).toFixed(1)} KB)`);

// Persist new version to version.json only after full build & zip succeed
verCfg.version = nextVer;
verCfg.lastBuiltAt = new Date().toISOString();
fs.writeFileSync(versionJsonPath, JSON.stringify(verCfg, null, 2) + '\n', 'utf8');
console.log(`[6/6] Updated version.json -> version "${nextVer}". Next build will automatically bump to v${((Math.round(parseFloat(nextVer) * 10) + 1) / 10).toFixed(1)}.`);
