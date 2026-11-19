'use strict';
/* Hands desktop shell. Loads the unmodified index.html in a locked-down
   window: no Node in the page, sandboxed renderer, no preload, no new
   windows, no navigation away from the app. http(s) links open in the
   OS default browser instead. */
const { app, BrowserWindow, shell, session } = require('electron');
const path = require('path');
const { pathToFileURL } = require('url');

const APP_FILE = path.join(__dirname, '..', 'index.html');
const APP_URL = pathToFileURL(APP_FILE);
const ALLOWED_PERMISSIONS = new Set(['clipboard-sanitized-write']);

function isAppUrl(raw) {
  try {
    const u = new URL(raw);
    return u.protocol === 'file:' && u.pathname === APP_URL.pathname;
  } catch (e) {
    return false;
  }
}

function openExternalIfWeb(raw) {
  try {
    const u = new URL(raw);
    if (u.protocol === 'http:' || u.protocol === 'https:') shell.openExternal(u.href);
  } catch (e) { /* not a URL: ignore */ }
}

app.on('web-contents-created', (_e, contents) => {
  contents.setWindowOpenHandler(({ url }) => {
    openExternalIfWeb(url);
    return { action: 'deny' };
  });
  contents.on('will-navigate', (e, url) => {
    if (isAppUrl(url)) return;
    e.preventDefault();
    openExternalIfWeb(url);
  });
  contents.on('will-redirect', (e, url) => {
    if (!isAppUrl(url)) e.preventDefault();
  });
  contents.on('will-attach-webview', (e) => e.preventDefault());
});

function createWindow() {
  const win = new BrowserWindow({
    width: 1280,
    height: 900,
    title: 'Hands',
    webPreferences: {
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
      webSecurity: true
    }
  });
  win.loadFile(APP_FILE);
}

if (!app.requestSingleInstanceLock()) {
  app.quit();
} else {
  app.on('second-instance', () => {
    const win = BrowserWindow.getAllWindows()[0];
    if (win) { if (win.isMinimized()) win.restore(); win.focus(); }
  });
  app.whenReady().then(() => {
    session.defaultSession.setPermissionRequestHandler((_wc, permission, cb) => cb(ALLOWED_PERMISSIONS.has(permission)));
    session.defaultSession.setPermissionCheckHandler((_wc, permission) => ALLOWED_PERMISSIONS.has(permission));
    createWindow();
    app.on('activate', () => {
      if (BrowserWindow.getAllWindows().length === 0) createWindow();
    });
  });
  app.on('window-all-closed', () => {
    if (process.platform !== 'darwin') app.quit();
  });
}
