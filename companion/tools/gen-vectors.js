'use strict';
/* Parity-vector generator for the Windows companion.
   Runs scripted scenarios through the real HANDS CORE engine (extracted
   from ../../index.html) and records the fired entries as JSON. The C#
   port replays the same vectors (companion/tests/logic) and must produce
   identical results, which is what proves the port kept the semantics.
   Zero-dependency plain Node, like the rest of this repository.
   Run: node companion/tools/gen-vectors.js
   Output: companion/tests/vectors.json */
const fs = require('fs');
const path = require('path');

const htmlPath = path.join(__dirname, '..', '..', 'index.html');
const html = fs.readFileSync(htmlPath, 'utf8');
const m = html.match(/\/\* ==== HANDS CORE BEGIN ==== \*\/([\s\S]*?)\/\* ==== HANDS CORE END ==== \*\//);
if (!m) { console.error('FAIL: HANDS CORE section not found in index.html'); process.exit(1); }
global.window = {};
eval(m[1]);
const HC = global.window.HC;
if (!HC) { console.error('FAIL: HC not defined by core section'); process.exit(1); }

const KEYS = { kind: 'keys', keys: ['Ctrl', 'C'] };
function rule(id, kind, buttons, extra, action) {
  const trigger = Object.assign({ kind, buttons }, extra || {});
  return { id, name: id, enabled: true, trigger, action: action || KEYS };
}
/* Step encoding in the vector file:
   ["down", button, time] / ["up", button, time] / ["advance", time] */
function S() { return Array.prototype.slice.call(arguments); }

const scenarios = [];
function scenario(name, rules, steps) {
  const engine = HC.createEngine(rules);
  const fired = [];
  for (const st of steps) {
    let out;
    if (st[0] === 'advance') out = engine.advanceTo(st[1]);
    else out = engine.handleEvent({ button: st[1], type: st[0], time: st[2] });
    for (const f of out) {
      fired.push({ ruleId: f.ruleId, triggerKind: f.triggerKind, time: f.time, action: f.action });
    }
  }
  scenarios.push({ name, rules, steps, expectErrors: engine.errors.length, expectFired: fired });
}

/* ---- press ---- */
scenario('press-basic', [rule('p1', 'press', ['A'])], [
  S('down', 'A', 0), S('up', 'A', 10), S('down', 'A', 20), S('down', 'B', 30)
]);
scenario('press-dup-and-stray', [rule('p1', 'press', ['A'])], [
  S('down', 'A', 0), S('down', 'A', 50), S('up', 'ZZ', 60), S('up', 'A', 70), S('down', 'A', 80)
]);

/* ---- chord ---- */
scenario('chord-window-inclusive', [rule('c1', 'chord', ['A', 'B'], { windowMs: 100 })], [
  S('down', 'A', 0), S('down', 'B', 100)
]);
scenario('chord-window-exclusive', [rule('c1', 'chord', ['A', 'B'], { windowMs: 100 })], [
  S('down', 'A', 0), S('down', 'B', 101), S('up', 'A', 150), S('up', 'B', 160),
  S('down', 'A', 200), S('down', 'B', 250)
]);
scenario('chord-modifier-style', [rule('c1', 'chord', ['A', 'B'], { windowMs: 0 })], [
  S('down', 'A', 0), S('down', 'B', 5000)
]);
scenario('chord-latch-and-rearm', [rule('c1', 'chord', ['A', 'B'], { windowMs: 100 })], [
  S('down', 'A', 0), S('down', 'B', 50), S('down', 'B', 60),
  S('up', 'A', 120), S('down', 'A', 140), S('down', 'A', 160)
]);
scenario('chord-three-members', [rule('c1', 'chord', ['A', 'B', 'C'], { windowMs: 100 })], [
  S('down', 'A', 0), S('down', 'B', 40), S('down', 'C', 90)
]);
scenario('chord-member-released-early', [rule('c1', 'chord', ['A', 'B', 'C'], { windowMs: 100 })], [
  S('down', 'A', 0), S('up', 'A', 30), S('down', 'B', 40), S('down', 'C', 90)
]);

/* ---- longpress ---- */
scenario('longpress-threshold', [rule('l1', 'longpress', ['A'], { holdMs: 500 })], [
  S('down', 'A', 0), S('advance', 499), S('advance', 500), S('advance', 2000),
  S('up', 'A', 2100), S('down', 'A', 3000), S('advance', 3500)
]);
scenario('longpress-early-release-cancels', [rule('l1', 'longpress', ['A'], { holdMs: 500 })], [
  S('down', 'A', 0), S('up', 'A', 499), S('advance', 5000)
]);
scenario('longpress-release-at-threshold', [rule('l1', 'longpress', ['A'], { holdMs: 500 })], [
  S('down', 'A', 0), S('up', 'A', 500)
]);
scenario('longpress-chord-form', [rule('l1', 'longpress', ['A', 'B'], { holdMs: 400 })], [
  S('down', 'A', 0), S('down', 'B', 100), S('advance', 499), S('advance', 500)
]);
scenario('longpress-chord-repress-restarts', [rule('l1', 'longpress', ['A', 'B'], { holdMs: 400 })], [
  S('down', 'A', 0), S('down', 'B', 100), S('up', 'B', 450), S('down', 'B', 600),
  S('advance', 999), S('advance', 1000)
]);

/* ---- double ---- */
scenario('double-inclusive', [rule('d1', 'double', ['A'], { intervalMs: 350 })], [
  S('down', 'A', 0), S('up', 'A', 50), S('down', 'A', 350)
]);
scenario('double-exclusive-then-new-pair', [rule('d1', 'double', ['A'], { intervalMs: 350 })], [
  S('down', 'A', 0), S('up', 'A', 50), S('down', 'A', 351), S('up', 'A', 400), S('down', 'A', 600)
]);
scenario('double-after-fire-new-pair', [rule('d1', 'double', ['A'], { intervalMs: 350 })], [
  S('down', 'A', 0), S('up', 'A', 40), S('down', 'A', 100), S('up', 'A', 140),
  S('down', 'A', 200), S('up', 'A', 240), S('down', 'A', 300)
]);
scenario('double-dup-down-ignored', [rule('d1', 'double', ['A'], { intervalMs: 350 })], [
  S('down', 'A', 0), S('down', 'A', 100), S('up', 'A', 150), S('down', 'A', 400)
]);

/* ---- sequence ---- */
scenario('sequence-basic', [rule('s1', 'sequence', ['A', 'B', 'C'], { windowMs: 800 })], [
  S('down', 'A', 0), S('down', 'B', 100), S('down', 'C', 200)
]);
scenario('sequence-boundary-inclusive', [rule('s1', 'sequence', ['A', 'B', 'C'], { windowMs: 800 })], [
  S('down', 'A', 0), S('down', 'B', 800), S('down', 'C', 1600)
]);
scenario('sequence-gap-breaks', [rule('s1', 'sequence', ['A', 'B', 'C'], { windowMs: 800 })], [
  S('down', 'A', 0), S('down', 'B', 801), S('down', 'C', 900), S('advance', 5000)
]);
scenario('sequence-wrong-button-resets', [rule('s1', 'sequence', ['A', 'B', 'C'], { windowMs: 800 })], [
  S('down', 'A', 0), S('down', 'X', 100), S('down', 'B', 200), S('down', 'C', 300)
]);
scenario('sequence-first-member-restarts', [rule('s1', 'sequence', ['A', 'B', 'C'], { windowMs: 800 })], [
  S('down', 'A', 0), S('up', 'A', 50), S('down', 'A', 100), S('down', 'B', 200), S('down', 'C', 300)
]);
scenario('sequence-progress-expires', [rule('s1', 'sequence', ['A', 'B', 'C'], { windowMs: 800 })], [
  S('down', 'A', 0), S('advance', 801), S('down', 'B', 900), S('down', 'C', 1000)
]);
scenario('sequence-repeated-member', [rule('s1', 'sequence', ['A', 'B', 'A'], { windowMs: 800 })], [
  S('down', 'A', 0), S('up', 'A', 50), S('down', 'B', 100), S('up', 'B', 150), S('down', 'A', 200)
]);

/* ---- actions passthrough ---- */
scenario('action-macro-passthrough', [
  rule('m1', 'press', ['A'], null, { kind: 'macro', steps: [{ keys: ['Ctrl', 'S'], delayMs: 50 }, { keys: ['Enter'], delayMs: 150 }] })
], [S('down', 'A', 0)]);
scenario('action-none', [
  rule('n1', 'press', ['A'], null, { kind: 'none' })
], [S('down', 'A', 0), S('up', 'A', 10)]);
scenario('action-keys-multi', [
  rule('k1', 'press', ['A'], null, { kind: 'keys', keys: ['Ctrl', 'Shift', 'P'] })
], [S('down', 'A', 0)]);

/* ---- independence, ordering, disabled, invalid ---- */
scenario('multi-rule-order', [
  rule('p1', 'press', ['A']), rule('c1', 'chord', ['A', 'B'], { windowMs: 100 })
], [S('down', 'A', 0), S('down', 'B', 50)]);
scenario('disabled-rule-never-fires', [
  { id: 'off', name: 'off', enabled: false, trigger: { kind: 'press', buttons: ['A'] }, action: KEYS },
  rule('p1', 'press', ['A'])
], [S('down', 'A', 0)]);
scenario('invalid-rule-reported-valid-still-works', [
  { bogus: true }, rule('p1', 'press', ['A'])
], [S('down', 'A', 0)]);

/* ---- gamepad-style ids, exactly as the studio feeds them ---- */
scenario('gamepad-ids-chord', [
  rule('g1', 'chord', ['g0b4', 'g0b5'], { windowMs: 0 }, { kind: 'keys', keys: ['Esc'] })
], [S('down', 'g0b4', 0), S('down', 'g0b5', 1200), S('up', 'g0b4', 1300), S('down', 'g0b4', 1400)]);

/* ---- out-of-order time clamping ---- */
scenario('time-clamped', [rule('p1', 'press', ['A'])], [
  S('down', 'A', 100), S('up', 'A', 90), S('down', 'A', 50)
]);

const out = {
  generatedBy: 'companion/tools/gen-vectors.js',
  source: 'HANDS CORE in index.html (HC ' + HC.version + ')',
  scenarios
};
const dest = path.join(__dirname, '..', 'tests', 'vectors.json');
fs.writeFileSync(dest, JSON.stringify(out, null, 2) + '\n');
console.log('wrote ' + dest + ' with ' + scenarios.length + ' scenarios');
