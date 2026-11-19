'use strict';
/* Hands UI test: drives the real index.html in Electron.
   Exercises: starter profile render, keyboard-test input through the real
   window event path (chord / double / long-press / sequence), the rule
   editor (picker + key capture), persistence across reload, language
   switch, WebXR controller input (with a fake navigator.xr), and captures a screenshot into docs/.
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

    /* --- share link: button produces a link; opening it imports the profile --- */
    check('share button exists', await run('!!document.getElementById("shareBtn")'));
    await run('window.__sharedUrl = ""; Object.defineProperty(navigator, "clipboard", { configurable: true, value: { writeText: function (s) { window.__sharedUrl = s; return Promise.resolve(); } } })');
    await run('document.getElementById("shareBtn").click()');
    await sleep(80);
    check('share button copies a #s= link', await run('window.__sharedUrl.indexOf("#s=") > 0'));
    check('share success is reported', await run('document.getElementById("profileErr").textContent.length > 0'));
    const profilesBefore = await run('JSON.parse(localStorage.getItem("hands.studio.v1")).profiles.length');
    const sharedUrl = await run('window.__sharedUrl');
    await win.loadURL(sharedUrl.split('#')[0] + '?fresh=1'); /* a bare hash change would not reload the page */
    await win.loadURL(sharedUrl);
    await sleep(500);
    check('opening the link imports one profile', await run('JSON.parse(localStorage.getItem("hands.studio.v1")).profiles.length') === profilesBefore + 1);
    check('imported profile becomes active', await run('(function () { var d = JSON.parse(localStorage.getItem("hands.studio.v1")); return d.activeId === d.profiles[d.profiles.length - 1].id; })()'));
    check('imported profile keeps its rules', await run('document.querySelectorAll("#ruleList .rule-card").length === 6'));
    check('imported message is shown', await run('document.getElementById("profileErr").textContent.length > 0'));
    check('fragment is removed after import', await run('location.hash.indexOf("s=") === -1'));

    /* --- broken share link: a reason is shown and nothing is imported --- */
    const profilesNow = await run('JSON.parse(localStorage.getItem("hands.studio.v1")).profiles.length');
    const fileUrl = require('url').pathToFileURL(path.join(root, 'index.html')).href;
    await win.loadURL(fileUrl + '?fresh=2');
    await win.loadURL(fileUrl + '#s=!!!broken!!!');
    await sleep(500);
    check('broken link shows why it failed', await run('document.getElementById("profileErr").textContent.indexOf("読み込めませんでした") >= 0'));
    check('broken link imports nothing', await run('JSON.parse(localStorage.getItem("hands.studio.v1")).profiles.length') === profilesNow);
    await win.loadURL(fileUrl);
    await sleep(500);

    /* --- language switch --- */
    await run('var l = document.getElementById("langSel"); l.value = "en"; l.dispatchEvent(new Event("change", {bubbles:true}))');
    await sleep(50);
    check('language switches to English', await run('document.querySelector("[data-i18n=\\"devices\\"]").textContent === "Devices"'));
    await run('var l2 = document.getElementById("langSel"); l2.value = "ja"; l2.dispatchEvent(new Event("change", {bubbles:true}))');
    await sleep(50);
    check('language switches back to Japanese', await run('document.querySelector("[data-i18n=\\"devices\\"]").textContent === "デバイス"'));

    /* --- WebXR: Quest Touch controllers are read from an immersive session's
       input sources (they never appear in navigator.getGamepads()). A fake
       navigator.xr is installed before the page's own scripts run. --- */
    check('XR entry hidden without WebXR support', await run('document.getElementById("xrBox").style.display === "none"'));
    const fakeXR = `(function () {
      var X = window.__xr = { sources: [], frame: null, green: 0 };
      var listeners = {};
      var session = X.session = {
        inputSources: X.sources, renderState: { baseLayer: null },
        updateRenderState: function (s) { this.renderState.baseLayer = s.baseLayer; },
        addEventListener: function (n, f) { listeners[n] = f; },
        requestAnimationFrame: function (cb) { X.frame = cb; return 1; },
        end: function () { if (listeners.end) listeners.end({}); return Promise.resolve(); }
      };
      X.step = function () { var cb = X.frame; X.frame = null; if (cb) cb(performance.now(), { session: session }); };
      window.XRWebGLLayer = function () { this.framebuffer = null; };
      var getContext = HTMLCanvasElement.prototype.getContext;
      HTMLCanvasElement.prototype.getContext = function (kind) {
        if (kind !== 'webgl') return getContext.apply(this, arguments);
        return { FRAMEBUFFER: 36160, COLOR_BUFFER_BIT: 16384, bindFramebuffer: function () {}, clear: function () {},
                 clearColor: function (r, g) { X.green = g; } };
      };
      Object.defineProperty(navigator, 'xr', { configurable: true, value: {
        isSessionSupported: function (mode) { return Promise.resolve(mode === 'immersive-vr'); },
        requestSession: function () { return Promise.resolve(session); }
      } });
    })();`;
    win.webContents.debugger.attach('1.3');
    await win.webContents.debugger.sendCommand('Page.enable');
    await win.webContents.debugger.sendCommand('Page.addScriptToEvaluateOnNewDocument', { source: fakeXR });
    await run('(function () { var d = JSON.parse(localStorage.getItem("hands.studio.v1")); var p = d.profiles.filter(function (x) { return x.id === d.activeId; })[0];' +
      ' p.rules.push({ id: "rule-xr", name: "XR Jump", enabled: true, trigger: { kind: "press", buttons: ["g9b4"] }, action: { kind: "keys", keys: ["Space"] } });' +
      ' p.rules.push({ id: "rule-xr-hold", name: "XR Hold", enabled: true, trigger: { kind: "longpress", buttons: ["g9b5"], holdMs: 300 }, action: { kind: "keys", keys: ["Enter"] } });' +
      ' localStorage.setItem("hands.studio.v1", JSON.stringify(d)); })()');
    await win.reload();
    await sleep(400);
    check('XR entry shown when immersive-vr is supported', await run('document.getElementById("xrBox").style.display === "" && document.getElementById("xrBtn").textContent.indexOf("VR") >= 0'));
    await run('document.getElementById("xrBtn").click()');
    await sleep(50);
    check('XR session starts on click', await run('typeof __xr.frame === "function" && document.getElementById("xrBtn").textContent === "VR を終える"'));
    await run('__xr.sources.push({ handedness: "right", gamepad: { mapping: "xr-standard", buttons: [0,1,2,3,4,5,6].map(function () { return { pressed: false, value: 0 }; }), axes: [0, 0, 0, 0] } }); __xr.step()');
    check('right Touch controller appears as a pad with Quest names', await run('document.getElementById("padList").textContent.indexOf("WebXR right") >= 0 && document.querySelector("#padList select").value === "quest3-r" && document.getElementById("padList").textContent.indexOf("Thumbstick Click") >= 0'));
    check('XR pad shows only the controller\'s own buttons on first connect', await run('document.querySelector("#padList .btn-grid").children.length === 7'));
    await run('__xr.sources[0].gamepad.buttons[4].pressed = true; __xr.step()');
    check('XR button press fires the bound rule', await run('document.getElementById("log").textContent.indexOf("XR Jump") >= 0'));
    check('XR view flashes on fire', await run('__xr.step(), __xr.green > 0.5'));
    await run('__xr.sources[0].gamepad.buttons[5].pressed = true; __xr.step()');
    await run('__xr.session.end()');
    await sleep(500);
    check('ending VR releases the pad and resets the button', await run('document.getElementById("padList").textContent.indexOf("未接続") >= 0 && document.getElementById("xrBtn").textContent === "VR に入って読む（WebXR）"'));
    check('a button held when VR ends is released (long press does not fire later)', await run('document.getElementById("log").textContent.indexOf("XR Hold") < 0'));
    win.webContents.debugger.detach();

    check('no page console errors', results.consoleErrors.length === 0);
  } catch (err) {
    console.error('UI EXCEPTION:', err && err.message);
    check('no exception', false);
  }

  const failed = results.checks.filter((c) => !c.ok).length;
  console.log('HANDS-UI-RESULT ' + JSON.stringify({ total: results.checks.length, failed, consoleErrors: results.consoleErrors }));
  app.exit(failed ? 1 : 0);
});
