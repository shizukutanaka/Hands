'use strict';
/* Hands UI test: drives the real index.html in Electron.
   Exercises: starter profile render, keyboard-test input through the real
   window event path (chord / double / long-press / sequence), the rule
   editor (picker + key capture), persistence across reload, language
   switch, and captures a screenshot into docs/.
   Run:
     xvfb-run -a <electron-binary> tests/ui-main.js --no-sandbox
   Prints a JSON result line; exit code 0 on success. */
const { app, BrowserWindow } = require('electron');
const fs = require('fs');
const path = require('path');

const root = path.join(__dirname, '..');
const docsDir = path.join(root, 'docs');
const results = { checks: [], consoleErrors: [] };
function check(name, cond) {
  results.checks.push({ name, ok: !!cond });
  if (!cond) console.error('UI FAIL: ' + name);
}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

app.disableHardwareAcceleration();
app.setPath('userData', fs.mkdtempSync(path.join(require('os').tmpdir(), 'hands-ui-profile-')));

app.whenReady().then(async () => {
  const win = new BrowserWindow({
    width: 1280, height: 950, show: true,
    webPreferences: { contextIsolation: true, backgroundThrottling: false }
  });
  win.webContents.on('console-message', (e, level, msg) => {
    if (level >= 2) results.consoleErrors.push(String(msg));
  });
  await win.loadFile(path.join(root, 'index.html'));
  const run = (code) => win.webContents.executeJavaScript(code, true);
  const key = (type, code, keyName, mods) => run(
    'window.dispatchEvent(new KeyboardEvent(' + JSON.stringify(type) +
    ', Object.assign({code:' + JSON.stringify(code) + ', key:' + JSON.stringify(keyName) + ', bubbles:true}, ' +
    JSON.stringify(mods || {}) + ')))'
  );

  try {
    /* --- boot & starter profile --- */
    check('HC namespace exists', await run('typeof HC === "object" && HC.version === "1.0.0"'));
    check('starter profile renders 5 rules', await run('document.querySelectorAll("#ruleList .rule-card").length === 5'));
    check('keyboard pad renders 8 cells', await run('document.querySelectorAll("#kbGrid .btn-cell").length === 8'));
    check('no-pad hint visible without hardware', await run('document.getElementById("noPadHint").style.display !== "none"'));

    /* --- chord: A + S together -> Demo Copy (Ctrl+C) --- */
    await key('keydown', 'KeyA', 'a'); await sleep(30);
    await key('keydown', 'KeyS', 's'); await sleep(60);
    check('chord fires Demo Copy', await run('document.getElementById("log").textContent.indexOf("Demo Copy") >= 0'));
    check('flash shows Ctrl + C', await run('document.getElementById("flash").textContent.indexOf("Ctrl") >= 0'));
    await key('keyup', 'KeyA', 'a'); await key('keyup', 'KeyS', 's'); await sleep(30);

    /* --- double press: F twice -> Demo Paste --- */
    await key('keydown', 'KeyF', 'f'); await sleep(40); await key('keyup', 'KeyF', 'f'); await sleep(60);
    await key('keydown', 'KeyF', 'f'); await sleep(60);
    check('double press fires Demo Paste', await run('document.getElementById("log").textContent.indexOf("Demo Paste") >= 0'));
    await key('keyup', 'KeyF', 'f'); await sleep(30);

    /* --- long press: hold J -> Demo Confirm --- */
    await key('keydown', 'KeyJ', 'j'); await sleep(800);
    check('long press fires Demo Confirm', await run('document.getElementById("log").textContent.indexOf("Demo Confirm") >= 0'));
    await key('keyup', 'KeyJ', 'j'); await sleep(30);

    /* --- sequence: A, S, D in order -> Demo Save (macro) --- */
    await key('keydown', 'KeyA', 'a'); await sleep(40); await key('keyup', 'KeyA', 'a'); await sleep(60);
    await key('keydown', 'KeyS', 's'); await sleep(40); await key('keyup', 'KeyS', 's'); await sleep(60);
    await key('keydown', 'KeyD', 'd'); await sleep(40); await key('keyup', 'KeyD', 'd');
    await sleep(600); /* macro steps: 50ms + 150ms delays */
    check('sequence fires Demo Save macro', await run('document.getElementById("log").textContent.indexOf("Demo Save") >= 0'));
    check('macro steps are logged', await run('document.getElementById("log").textContent.indexOf("Ctrl + S") >= 0'));

    /* --- screenshot of the main view with a populated log --- */
    fs.mkdirSync(docsDir, { recursive: true });
    const img = await win.webContents.capturePage();
    fs.writeFileSync(path.join(docsDir, 'screenshot-main.png'), img.toPNG());
    check('screenshot written', fs.statSync(path.join(docsDir, 'screenshot-main.png')).size > 10000);

    /* --- editor: add a press rule for D with captured Ctrl+X --- */
    await run('document.getElementById("addRuleBtn").click()');
    await sleep(100);
    check('editor opens', await run('document.getElementById("editor").classList.contains("open")'));
    await run('var n = document.getElementById("edName"); n.value = "UI Test Rule"; n.dispatchEvent(new Event("input", {bubbles:true}))');
    await run('var k = document.getElementById("edKind"); k.value = "press"; k.dispatchEvent(new Event("change", {bubbles:true}))');
    await sleep(50);
    await run('document.querySelectorAll("#buttonPicker button")[2].click()'); /* k2 = D */
    await run('document.getElementById("captureKeysBtn").click()');
    await sleep(50);
    await key('keydown', '', 'X', { ctrlKey: true });
    await sleep(50);
    check('captured keys shown as chips', await run('document.getElementById("edKeys").textContent.indexOf("Ctrl") >= 0 && document.getElementById("edKeys").textContent.indexOf("X") >= 0'));
    await run('document.getElementById("saveRuleBtn").click()');
    await sleep(100);
    check('rule saved and listed (6 rules)', await run('document.querySelectorAll("#ruleList .rule-card").length === 6'));
    check('editor closed after save', await run('!document.getElementById("editor").classList.contains("open")'));
    check('new rule fires from real input', await (async () => {
      await key('keydown', 'KeyD', 'd'); await sleep(60);
      const hit = await run('document.getElementById("log").textContent.indexOf("UI Test Rule") >= 0');
      await key('keyup', 'KeyD', 'd');
      return hit;
    })());

    /* --- persistence across reload --- */
    check('localStorage holds the profile', await run('(JSON.parse(localStorage.getItem("hands.studio.v1")).profiles[0].rules.length) === 6'));
    await win.reload();
    await sleep(400);
    check('rules survive reload', await run('document.querySelectorAll("#ruleList .rule-card").length === 6'));

    /* --- export shape (core, in page context) --- */
    check('export JSON is a Hands file', await run('HC.exportProfile(JSON.parse(localStorage.getItem("hands.studio.v1")).profiles[0]).indexOf("\\"app\\": \\"hands\\"") >= 0'));

    /* --- language switch --- */
    await run('var l = document.getElementById("langSel"); l.value = "en"; l.dispatchEvent(new Event("change", {bubbles:true}))');
    await sleep(50);
    check('language switches to English', await run('document.querySelector("[data-i18n=\\"devices\\"]").textContent === "Devices"'));
    await run('var l2 = document.getElementById("langSel"); l2.value = "ja"; l2.dispatchEvent(new Event("change", {bubbles:true}))');
    await sleep(50);
    check('language switches back to Japanese', await run('document.querySelector("[data-i18n=\\"devices\\"]").textContent === "デバイス"'));

    check('no page console errors', results.consoleErrors.length === 0);
  } catch (err) {
    console.error('UI EXCEPTION:', err && err.message);
    check('no exception', false);
  }

  const failed = results.checks.filter((c) => !c.ok).length;
  console.log('HANDS-UI-RESULT ' + JSON.stringify({ total: results.checks.length, failed, consoleErrors: results.consoleErrors }));
  app.exit(failed ? 1 : 0);
});
