'use strict';
/* Hands core tests. Zero-dependency: plain Node.
   Extracts the HANDS CORE section from index.html and exercises it.
   Run: node tests/run-tests.js */
const fs = require('fs');
const path = require('path');

const html = fs.readFileSync(path.join(__dirname, '..', 'index.html'), 'utf8');
const m = html.match(/\/\* ==== HANDS CORE BEGIN ==== \*\/([\s\S]*?)\/\* ==== HANDS CORE END ==== \*\//);
if (!m) { console.error('FAIL: HANDS CORE section not found in index.html'); process.exit(1); }
global.window = {};
eval(m[1]);
const HC = global.window.HC;
if (!HC) { console.error('FAIL: HC not defined by core section'); process.exit(1); }

let passed = 0, failed = 0;
function ok(cond, name) {
  if (cond) { passed++; }
  else { failed++; console.error('FAIL: ' + name); }
}
function eq(a, b, name) { ok(JSON.stringify(a) === JSON.stringify(b), name + ' (got ' + JSON.stringify(a) + ')'); }

const KEYS = { kind: 'keys', keys: ['Ctrl', 'C'] };
function rule(kind, buttons, extra, action) {
  const trigger = Object.assign({ kind, buttons }, extra || {});
  return { id: 'r-' + kind + '-' + buttons.join('_'), name: kind, enabled: true, trigger, action: action || KEYS };
}
function down(e, b, t) { return e.handleEvent({ button: b, type: 'down', time: t }); }
function up(e, b, t) { return e.handleEvent({ button: b, type: 'up', time: t }); }

/* ---- namespace / config ---- */
eq(HC.version, '1.0.0', 'HC.version');
ok(HC.config.zeroDep && HC.config.singleFile && HC.config.localFirst && HC.config.zeroTelemetry, 'HC.config flags');
eq(HC.TRIGGER_KINDS, ['press', 'chord', 'longpress', 'double', 'sequence'], 'trigger kinds');

/* ---- press ---- */
{
  const e = HC.createEngine([rule('press', ['A'])]);
  eq(down(e, 'A', 0).length, 1, 'press: down fires');
  eq(up(e, 'A', 10).length, 0, 'press: up does not fire');
  eq(down(e, 'A', 20).length, 1, 'press: second press fires');
  eq(down(e, 'B', 30).length, 0, 'press: other button does not fire');
  const e2 = HC.createEngine([rule('press', ['A'])]);
  down(e2, 'A', 0);
  eq(down(e2, 'A', 50).length, 0, 'press: duplicate down without up is not a new edge');
  eq(up(e2, 'ZZ', 60).length, 0, 'press: stray up ignored');
}

/* ---- chord ---- */
{
  const mk = (w) => HC.createEngine([rule('chord', ['A', 'B'], { windowMs: w })]);
  let e = mk(100);
  eq(down(e, 'A', 0).length, 0, 'chord: first member alone does not fire');
  eq(down(e, 'B', 50).length, 1, 'chord: completes within window');
  e = mk(100);
  down(e, 'A', 0);
  eq(down(e, 'B', 100).length, 1, 'chord: spread == window fires (inclusive)');
  e = mk(100);
  down(e, 'A', 0);
  eq(down(e, 'B', 101).length, 0, 'chord: spread == window+1 does not fire');
  e = mk(0);
  down(e, 'A', 0);
  eq(down(e, 'B', 5000).length, 1, 'chord: window 0 is modifier style (any timing)');
  /* latch: no refire while held; release re-arms */
  e = mk(100);
  down(e, 'A', 0); down(e, 'B', 50);
  eq(down(e, 'B', 60).length, 0, 'chord: duplicate down while latched does not refire');
  up(e, 'A', 120);
  eq(down(e, 'A', 140).length, 1, 'chord: re-press after release can refire (spread 90 <= window)');
  eq(down(e, 'A', 160).length, 0, 'chord: still latched after refire');
  /* 3 members */
  e = HC.createEngine([rule('chord', ['A', 'B', 'C'], { windowMs: 100 })]);
  down(e, 'A', 0); down(e, 'B', 40);
  eq(down(e, 'C', 90).length, 1, 'chord: 3 members fire when all down');
  e = HC.createEngine([rule('chord', ['A', 'B', 'C'], { windowMs: 100 })]);
  down(e, 'A', 0); up(e, 'A', 30); down(e, 'B', 40);
  eq(down(e, 'C', 90).length, 0, 'chord: member released before completion -> no fire');
}

/* ---- longpress ---- */
{
  const mk = () => HC.createEngine([rule('longpress', ['A'], { holdMs: 500 })]);
  let e = mk();
  down(e, 'A', 0);
  eq(e.advanceTo(499).length, 0, 'longpress: not yet at holdMs-1');
  const f = e.advanceTo(500);
  eq(f.length, 1, 'longpress: fires at exactly holdMs (inclusive)');
  eq(f[0].time, 500, 'longpress: fired time is the threshold moment');
  eq(e.advanceTo(2000).length, 0, 'longpress: no refire while still held');
  up(e, 'A', 2100); down(e, 'A', 3000);
  eq(e.advanceTo(3500).length, 1, 'longpress: re-arms after release and fires again');
  e = mk();
  down(e, 'A', 0); up(e, 'A', 499);
  eq(e.advanceTo(5000).length, 0, 'longpress: release before holdMs cancels');
  e = mk();
  down(e, 'A', 0);
  const f2 = up(e, 'A', 500);
  eq(f2.length, 1, 'longpress: release exactly at holdMs still counts (pre-event check)');
  eq(f2[0].time, 500, 'longpress: release-at-threshold fired time');
  /* chord form: timer starts when the set completes */
  e = HC.createEngine([rule('longpress', ['A', 'B'], { holdMs: 400 })]);
  down(e, 'A', 0); down(e, 'B', 100);
  eq(e.advanceTo(499).length, 0, 'longpress chord: 399ms after completion not yet');
  eq(e.advanceTo(500).length, 1, 'longpress chord: fires 400ms after completion');
  e = HC.createEngine([rule('longpress', ['A', 'B'], { holdMs: 400 })]);
  down(e, 'A', 0); down(e, 'B', 100); up(e, 'B', 450);
  eq(e.advanceTo(5000).length, 0, 'longpress chord: member release cancels');
  e = HC.createEngine([rule('longpress', ['A', 'B'], { holdMs: 400 })]);
  down(e, 'A', 0); down(e, 'B', 100); up(e, 'B', 450); down(e, 'B', 600);
  eq(e.advanceTo(999).length, 0, 'longpress chord: restarted hold not yet complete');
  eq(e.advanceTo(1000).length, 1, 'longpress chord: re-press restarts the hold');
}

/* ---- double ---- */
{
  const mk = () => HC.createEngine([rule('double', ['A'], { intervalMs: 350 })]);
  let e = mk();
  down(e, 'A', 0); up(e, 'A', 50);
  eq(down(e, 'A', 350).length, 1, 'double: gap == interval fires (down-to-down, inclusive)');
  e = mk();
  down(e, 'A', 0); up(e, 'A', 50);
  eq(down(e, 'A', 351).length, 0, 'double: gap == interval+1 does not fire');
  up(e, 'A', 400);
  eq(down(e, 'A', 600).length, 1, 'double: expired press becomes the first of a new pair');
  e = mk();
  down(e, 'A', 0); up(e, 'A', 40); down(e, 'A', 100); /* fires */
  up(e, 'A', 140);
  eq(down(e, 'A', 200).length, 0, 'double: after firing, next press starts a new pair');
  up(e, 'A', 240);
  eq(down(e, 'A', 300).length, 1, 'double: new pair completes');
  e = mk();
  down(e, 'A', 0);
  eq(down(e, 'A', 100).length, 0, 'double: duplicate down (no up between) does not count');
  up(e, 'A', 150);
  eq(down(e, 'A', 400).length, 0, 'double: pair is measured from the real first down');
}

/* ---- sequence ---- */
{
  const mk = () => HC.createEngine([rule('sequence', ['A', 'B', 'C'], { windowMs: 800 })]);
  let e = mk();
  down(e, 'A', 0); down(e, 'B', 100);
  eq(down(e, 'C', 200).length, 1, 'sequence: in-order presses fire');
  e = mk();
  down(e, 'A', 0); down(e, 'B', 800);
  eq(down(e, 'C', 1600).length, 1, 'sequence: step gap == window counts (inclusive)');
  e = mk();
  down(e, 'A', 0);
  eq(down(e, 'B', 801).length, 0, 'sequence: step gap == window+1 breaks the chain');
  down(e, 'C', 900);
  eq(e.advanceTo(5000).length, 0, 'sequence: broken chain never fires');
  e = mk();
  down(e, 'A', 0); down(e, 'X', 100); down(e, 'B', 200);
  eq(down(e, 'C', 300).length, 0, 'sequence: wrong button resets progress');
  e = mk();
  down(e, 'A', 0); up(e, 'A', 50); down(e, 'A', 100); down(e, 'B', 200);
  eq(down(e, 'C', 300).length, 1, 'sequence: pressing the first member restarts the attempt');
  e = mk();
  down(e, 'A', 0);
  e.advanceTo(801); /* partial progress expires */
  down(e, 'B', 900);
  eq(down(e, 'C', 1000).length, 0, 'sequence: expired progress does not complete');
  e = HC.createEngine([rule('sequence', ['A', 'B', 'A'], { windowMs: 800 })]);
  down(e, 'A', 0); up(e, 'A', 50); down(e, 'B', 100); up(e, 'B', 150);
  eq(down(e, 'A', 200).length, 1, 'sequence: repeated member at the end works');
}

/* ---- independence & ordering ---- */
{
  const e = HC.createEngine([
    rule('press', ['A']),
    rule('chord', ['A', 'B'], { windowMs: 100 })
  ]);
  eq(down(e, 'A', 0).map((f) => f.triggerKind), ['press'], 'multi: press fires alone first');
  eq(down(e, 'B', 50).map((f) => f.triggerKind), ['chord'], 'multi: chord fires on completion');
  const e2 = HC.createEngine([
    { id: 'off', name: 'off', enabled: false, trigger: { kind: 'press', buttons: ['A'] }, action: KEYS },
    rule('press', ['A'])
  ]);
  eq(down(e2, 'A', 0).length, 1, 'multi: disabled rules never fire');
  const e3 = HC.createEngine([{ bogus: true }, rule('press', ['A'])]);
  ok(e3.errors.length >= 1, 'multi: invalid rules are reported in engine.errors');
  eq(down(e3, 'A', 0).length, 1, 'multi: valid rules still work beside invalid ones');
}

/* ---- time handling ---- */
{
  const e = HC.createEngine([rule('press', ['A'])]);
  const f = e.handleEvent({ button: 'A', type: 'down', time: 100 });
  eq(f[0].time, 100, 'time: event time is carried through');
  up(e, 'A', 90); /* out of order: clamped, still a valid up */
  const f2 = down(e, 'A', 50);
  eq(f2.length, 1, 'time: out-of-order events are clamped, not fatal');
  eq(f2[0].time, 100, 'time: clamped to the last seen time');
  eq(e.handleEvent({ button: 'A', type: 'sideways', time: 200 }).length, 0, 'time: malformed events ignored');
  eq(e.handleEvent(null).length, 0, 'time: null event ignored');
}

/* ---- normalization ---- */
{
  const errs = [];
  eq(HC.normalizeRule({ trigger: { kind: 'chord', buttons: ['A'] }, action: KEYS }, errs), null, 'norm: chord with 1 button rejected');
  ok(errs.length >= 1, 'norm: rejection carries an error message');
  eq(HC.normalizeRule({ trigger: { kind: 'press', buttons: ['A', 'B'] }, action: KEYS }, []), null, 'norm: press with 2 buttons rejected');
  eq(HC.normalizeRule({ trigger: { kind: 'nope', buttons: ['A'] }, action: KEYS }, []), null, 'norm: unknown kind rejected');
  eq(HC.normalizeRule({ trigger: { kind: 'longpress', buttons: ['A'], holdMs: 5 }, action: KEYS }, []), null, 'norm: holdMs below range rejected');
  eq(HC.normalizeRule({ trigger: { kind: 'press', buttons: ['A'] }, action: { kind: 'keys', keys: [] } }, []), null, 'norm: empty action keys rejected');
  eq(HC.normalizeRule({ trigger: { kind: 'press', buttons: ['A'] }, action: { kind: 'macro', steps: [{ keys: [], delayMs: 10 }] } }, []), null, 'norm: macro step without keys rejected');
  const d = HC.normalizeRule({ trigger: { kind: 'double', buttons: ['A'] }, action: KEYS }, []);
  eq(d.trigger.intervalMs, 350, 'norm: double interval default applied');
  const c0 = HC.normalizeRule({ trigger: { kind: 'chord', buttons: ['A', 'B'], windowMs: 0 }, action: KEYS }, []);
  eq(c0.trigger.windowMs, 0, 'norm: chord windowMs 0 preserved (modifier style)');
  const cd = HC.normalizeRule({ trigger: { kind: 'chord', buttons: ['A', 'A', 'B'] }, action: KEYS }, []);
  eq(cd.trigger.buttons, ['A', 'B'], 'norm: chord buttons deduped');
  const sq = HC.normalizeRule({ trigger: { kind: 'sequence', buttons: ['A', 'A', 'B'] }, action: KEYS }, []);
  eq(sq.trigger.buttons, ['A', 'A', 'B'], 'norm: sequence keeps repeated buttons');
  const mc = HC.normalizeRule({ trigger: { kind: 'press', buttons: ['A'] }, action: { kind: 'macro', steps: [{ keys: ['Ctrl', 'S'] }] } }, []);
  eq(mc.action.steps[0].delayMs, 50, 'norm: macro step delay default applied');
  const en = HC.normalizeRule({ trigger: { kind: 'press', buttons: ['A'] }, action: { kind: 'none' } }, []);
  eq(en.action, { kind: 'none' }, 'norm: none action');
  eq(en.enabled, true, 'norm: enabled defaults to true');
}

/* ---- profiles & JSON ---- */
{
  const profile = {
    id: 'p1', name: 'Test',
    rules: [rule('press', ['A']), { bogus: 1 }, rule('chord', ['A', 'B'], { windowMs: 0 })]
  };
  const n = HC.normalizeProfile(profile);
  eq(n.profile.rules.length, 2, 'profile: invalid rules dropped, valid kept');
  eq(n.errors.length, 1, 'profile: one error for the dropped rule');
  const json = HC.exportProfile(n.profile);
  const back = HC.importProfile(json);
  eq(back.errors, [], 'json: round trip has no errors');
  eq(back.profile.rules, n.profile.rules, 'json: export/import round trip preserves rules');
  eq(HC.importProfile('not json {').profile, null, 'json: garbage rejected');
  eq(HC.importProfile('{"app":"other","version":1,"profile":{"rules":[]}}').profile, null, 'json: foreign app rejected');
  eq(HC.importProfile('{"app":"hands","version":99,"profile":{"rules":[]}}').profile, null, 'json: newer version rejected');
  const bare = HC.importProfile(JSON.stringify({ name: 'Bare', rules: [rule('press', ['A'])] }));
  eq(bare.profile.rules.length, 1, 'json: bare profile object accepted');
}

/* ---- profile target filter (companion send-side filter) ---- */
{
  const noTarget = HC.normalizeProfile({ name: 'Old', rules: [rule('press', ['A'])] });
  eq(noTarget.errors, [], 'target: profile without target has no errors (back compat)');
  eq(noTarget.profile.target, undefined, 'target: absent target stays absent');

  const withTarget = HC.normalizeProfile({
    name: 'T', rules: [rule('press', ['A'])],
    target: { titleContains: '  Notepad  ', processName: 'notepad.exe' }
  });
  eq(withTarget.errors, [], 'target: valid target has no errors');
  eq(withTarget.profile.target, { titleContains: 'Notepad', processName: 'notepad' },
    'target: values trimmed, .exe stripped');

  const roundTrip = HC.importProfile(HC.exportProfile(withTarget.profile));
  eq(roundTrip.errors, [], 'target: export/import round trip has no errors');
  eq(roundTrip.profile.target, { titleContains: 'Notepad', processName: 'notepad' },
    'target: export/import round trip preserves target');
  const shareBack = HC.decodeShareLink(HC.encodeShareLink(withTarget.profile).fragment);
  eq(shareBack.profile.target, { titleContains: 'Notepad', processName: 'notepad' },
    'target: share link round trip preserves target');

  const empty = HC.normalizeProfile({ name: 'E', rules: [], target: {} });
  ok(empty.errors.length >= 1 && empty.profile.target === undefined, 'target: empty object rejected');
  const badPath = HC.normalizeProfile({ name: 'P', rules: [], target: { processName: 'C:\\apps\\notepad' } });
  ok(badPath.errors.length >= 1 && badPath.profile.target === undefined, 'target: process path rejected');
  const badWild = HC.normalizeProfile({ name: 'W', rules: [], target: { processName: 'note*' } });
  ok(badWild.errors.length >= 1, 'target: wildcard process rejected');
  const badTitle = HC.normalizeProfile({ name: 'B', rules: [], target: { titleContains: '' } });
  ok(badTitle.errors.length >= 1, 'target: empty titleContains rejected');
  const notObj = HC.normalizeProfile({ name: 'N', rules: [], target: 'notepad' });
  ok(notObj.errors.length >= 1, 'target: non-object target rejected');
  const titleOnly = HC.normalizeProfile({ name: 'TO', rules: [], target: { titleContains: 'Game' } });
  eq(titleOnly.profile.target, { titleContains: 'Game' }, 'target: title-only target accepted');
  const procOnly = HC.normalizeProfile({ name: 'PO', rules: [], target: { processName: 'GAME.EXE' } });
  eq(procOnly.profile.target, { processName: 'GAME' }, 'target: process-only target accepted, .EXE stripped');
}

/* ---- share links ---- */
{
  const src = {
    id: 'p-share', name: '共有テスト 🎮', rules: [
      rule('press', ['A']),
      rule('chord', ['A', 'B'], { windowMs: 0 }),
      rule('longpress', ['Grip'], { holdMs: 700 }),
      rule('double', ['X'], { intervalMs: 300 }),
      rule('sequence', ['A', 'B', 'A'], { windowMs: 900 }, { kind: 'macro', steps: [{ keys: ['Ctrl', 'S'], delayMs: 50 }, { keys: ['Enter'], delayMs: 150 }] }),
      { id: 'r-none', name: '', enabled: false, trigger: { kind: 'press', buttons: ['Y'] }, action: { kind: 'none' } }
    ]
  };
  const want = HC.normalizeProfile(src).profile;
  const enc = HC.encodeShareLink(src);
  ok(typeof enc.fragment === 'string' && enc.fragment.indexOf('s=') === 0, 'share: fragment starts with s=');
  ok(!/[+/=]/.test(enc.fragment.slice(2)), 'share: payload is base64url (no +, / or =)');
  eq(enc.errors, [], 'share: encode has no errors');

  const back = HC.decodeShareLink(enc.fragment);
  eq(back.errors, [], 'share: round trip has no errors');
  eq(back.profile, want, 'share: round trip preserves the profile (incl. UTF-8 name)');
  eq(HC.decodeShareLink('#' + enc.fragment).profile, want, 'share: accepts a bare hash');
  eq(HC.decodeShareLink('https://example.invalid/hands#' + enc.fragment).profile, want, 'share: accepts a full URL');
  eq(HC.decodeShareLink('file:///home/user/hands.html#' + enc.fragment).profile, want, 'share: accepts a file URL');

  eq(HC.decodeShareLink(''), null, 'share: empty input is not a share link');
  eq(HC.decodeShareLink(null), null, 'share: null is not a share link');
  eq(HC.decodeShareLink('https://example.invalid/hands'), null, 'share: URL without fragment is not a share link');
  eq(HC.decodeShareLink('#top'), null, 'share: foreign fragment is not a share link');

  const b64 = (s) => Buffer.from(s, 'utf8').toString('base64url');
  const broken = HC.decodeShareLink('#s=!!!not-base64!!!');
  eq(broken.profile, null, 'share: invalid characters rejected');
  ok(broken.errors.length > 0, 'share: invalid characters produce a reason');
  const truncated = HC.decodeShareLink(enc.fragment.slice(0, enc.fragment.length - 6));
  eq(truncated.profile, null, 'share: truncated link rejected');
  ok(truncated.errors.length > 0, 'share: truncated link produces a reason');
  eq(HC.decodeShareLink('#s=' + b64('this is not json')).profile, null, 'share: non-JSON payload rejected');
  eq(HC.decodeShareLink('#s=' + b64('{"app":"other","version":1,"profile":{"rules":[]}}')).profile, null, 'share: foreign app rejected');
  eq(HC.decodeShareLink('#s=' + b64('{"app":"hands","version":99,"profile":{"rules":[]}}')).profile, null, 'share: newer version rejected');
  const badUtf8 = HC.decodeShareLink('#s=' + Buffer.from([0xff, 0xfe, 0xfd]).toString('base64url'));
  eq(badUtf8.profile, null, 'share: invalid UTF-8 payload rejected');
  ok(badUtf8.errors.length > 0, 'share: invalid UTF-8 produces a reason');

  const big = { name: 'Big', rules: [] };
  for (let i = 0; i < 300; i++) big.rules.push(rule('press', ['B' + i], null, { kind: 'keys', keys: ['K' + i] }));
  const bigEnc = HC.encodeShareLink(big);
  eq(bigEnc.fragment, null, 'share: oversized profile is refused, not truncated');
  ok(bigEnc.errors.length > 0, 'share: oversized profile produces a reason');
  const longLink = HC.decodeShareLink('#s=' + 'A'.repeat(HC.SHARE_FRAGMENT_MAX_LENGTH));
  eq(longLink.profile, null, 'share: overlong link rejected');
  ok(longLink.errors.length > 0, 'share: overlong link produces a reason');
}

/* ---- presets ---- */
{
  eq(HC.guessPresetId('Oculus Touch Controller (Right)', 'xr-standard'), 'quest3-r', 'preset: Quest right');
  eq(HC.guessPresetId('Meta Quest Touch Plus Controller (Left)', 'xr-standard'), 'quest3-l', 'preset: Quest left');
  eq(HC.guessPresetId('Xbox Wireless Controller', 'standard'), 'standard', 'preset: standard mapping');
  eq(HC.guessPresetId('Unknown Device', ''), 'generic', 'preset: generic fallback');
  eq(HC.presetById('quest3-r').buttons[4], 'A', 'preset: Quest right button 4 is A');
  eq(HC.presetById('quest3-l').buttons[4], 'X', 'preset: Quest left button 4 is X');
  eq(HC.presetById('quest3-r').buttons[0], 'Trigger', 'preset: Quest trigger');
}

/* ---- descriptions ---- */
{
  ok(HC.describeTrigger({ kind: 'chord', buttons: ['A', 'B'], windowMs: 0 }).indexOf('any timing') >= 0, 'describe: modifier chord');
  ok(HC.describeAction({ kind: 'keys', keys: ['Ctrl', 'C'] }) === 'Ctrl+C', 'describe: keys action');
  ok(HC.describeAction({ kind: 'macro', steps: [{ keys: ['Ctrl', 'S'] }, { keys: ['Enter'] }] }).indexOf('macro') === 0, 'describe: macro action');
}

console.log('hands core tests: ' + passed + ' passed, ' + failed + ' failed');
process.exit(failed ? 1 : 0);
