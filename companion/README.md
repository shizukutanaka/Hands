# Hands companion (Windows, minimal)

The player for Hands profiles. The studio (`../index.html`) is where you
design and verify bindings; this small Windows program is what makes them
real: it reads a profile JSON the studio exported, watches your
controller, and sends the fired keys to the focused window at OS level.

It has **no settings UI and nothing to configure** — if a binding is
wrong, fix it in the studio and export again. It makes no network
requests, has no account, and stores nothing except a log file next to
the program.

日本語: Hands 本体（ブラウザ）で作ったプロファイルを、実際のアプリで
効かせるための Windows 用の小さな常駐プログラムです。設定の編集は
しません（それは本体の仕事）。プロファイル JSON を読み、コントローラ
入力を見張り、発火したキーをフォーカスのあるウィンドウへ送ります。

## Safety — what this is, and what it is not

- This is a tool that runs **on your own PC**, replaying **your own
  bindings** from **a profile file you made**, for accessibility and
  convenience — the same category as keyboard remappers.
- It is **not** a cheat tool. It contains no automation loops: nothing
  repeats, nothing fires on a timer, nothing reads the screen or another
  app's state. Every key it sends is the direct result of a button
  gesture you physically performed, matched against your profile.
  Macros are the studio's fixed key sequences with delays — nothing more.
- Some games (especially those with anti-cheat) block synthetic input or
  prohibit remapping tools in their terms. When a send is refused, the
  companion says so in its log with the target window's title instead of
  dropping keys silently — but whether using it with a given game is
  allowed is that game's rule, and your call.

## Build (nothing to install)

`Build.bat` compiles `src/*.cs` into `hands-companion.exe` using
`csc.exe`, the C# compiler that ships with Windows itself
(.NET Framework). No SDK, no downloads. The sources deliberately use
only C# 5 features so that in-box compiler accepts them.

## Run

1. Build once with `Build.bat`.
2. Export a profile from the studio.
3. Either drag-and-drop the profile JSON onto
   `Start-HandsCompanion.bat`, or save it as `profile.json` in this
   folder and double-click the .bat. Or from a terminal:
   `hands-companion.exe path\to\profile.json`
4. The window stays open and shows what fires. Everything is also
   written to `hands-companion.log` in this folder.
5. Stop with **Ctrl+C**, by closing the window, or with the panic chord
   **Ctrl+Alt+F12** (works even when a game has focus).

Useful flags:

- `--dry-run` — log exactly which key events *would* be sent, send
  nothing. Safe way to check a profile.
- `--selftest-send` — send one harmless Shift tap, report whether
  SendInput accepted it, and exit. This is the first-run injection
  check from the design memo.

Startup is strict on purpose: a profile with an invalid rule, or one
that uses a key this companion cannot send, is rejected with the reasons
listed — never partially applied.

## How your controller maps

Input comes from **XInput** (slots become `g0`–`g3`), translated to the
studio's standard-mapping button indices:

| Index | Button | Index | Button |
|---|---|---|---|
| b0 | A | b9 | Menu / Start |
| b1 | B | b10 | L3 (left stick click) |
| b2 | X | b11 | R3 (right stick click) |
| b3 | Y | b12–b15 | D-Pad Up / Down / Left / Right |
| b4 / b5 | LB / RB | b6 / b7 | LT / RT (past the press threshold) |
| b8 | View / Back | b16 | — (Guide cannot be read via XInput) |

Quest controllers connected to the PC (Link, SteamVR, …) appear as an
XInput pad with this standard layout, so author the profile against the
**standard** preset numbering when you mean to use it here. Two honest
mismatches with the studio: the studio's pad number is the browser's
connection order while XInput's is the player slot (identical when one
controller is connected), and the studio's on-screen keyboard test
buttons (`k0`–`k7`) are a browser-only fallback — the companion does not
feed them, so the starter profile is for in-browser practice only.
`sample-profile.json` in this folder is a standard-pad profile covering
all five trigger kinds, made for trying the companion.

Keys are sent with `SendInput` scan codes, as the design memo
prescribes, so games that read raw input accept them. `Ctrl`/`Alt`/
`Shift` mean the left modifiers; `Meta` is the left Windows key.
Punctuation follows the US keyboard layout (the studio stores the
character a key produces; a shifted character such as `!` is sent as its
base key — pair it with `Shift` in the binding, exactly as captured).
Keys with no scan-code equivalent (PrintScreen, Pause, media keys,
F13 and above) are rejected at startup, by name.

If a controller disconnects while a button is held, the companion
releases that button inside the engine, so a chord cannot latch forever
or a long press fire on its own later.

## Status and limits

- **Windows only, XInput devices only.** DirectInput-only devices are
  not read. Linux/macOS companions remain design-only
  (see `../docs/INJECTION.md`).
- **Not yet verified on a real Windows machine.** The detection engine
  is proven identical to the studio's by generated parity vectors, and
  all logic is unit-tested (below), but the XInput polling and SendInput
  paths have only been compile-checked, not run against real hardware.
  The check procedure is in the next section.
- No target-app filtering yet: keys go to whatever window has focus.
  Keep the profile's effects in mind when you alt-tab.

## Check procedure (real machine)

1. Copy this `companion` folder to the PC and run `Build.bat`.
   Expect: `Build OK: hands-companion.exe`, no errors.
2. Run `hands-companion.exe --selftest-send` in a terminal.
   Expect: `self-test OK`. (Do it with Notepad focused — a lone Shift
   tap changes nothing.)
3. Connect a standard (XInput) controller. Copy `sample-profile.json`
   to `profile.json` in this folder and run `Start-HandsCompanion.bat`.
   Expect: the startup log lists 5 rules and
   `controller connected: slot 0`.
4. Focus Notepad and try: **A** → a space appears. **Hold B ~1 s** →
   a newline (Enter at 600 ms). **Double-press X** with text on the
   clipboard → it pastes (Ctrl+V). **D-Pad Up, then Down** → Ctrl+S
   opens Notepad's save dialog, then Enter confirms it.
   **Hold LB, tap RB** → Esc (watch the log line `FIRED`).
5. Open `hands-companion.log` and confirm one `FIRED` line per binding,
   with no `injection problem` errors.
6. Hold **Ctrl+Alt+F12** → the companion logs the panic chord and stops.
7. Finally, export your own profile from the studio and repeat steps
   3–5 with it (or drag-and-drop the file onto the .bat).

## Development

Zero dependencies, plain C# (language level 5). The engine is a port of
HANDS CORE; parity is not asserted by hand but proven: 
`tools/gen-vectors.js` runs scripted scenarios through the real
JavaScript engine and records the results, and the C# tests replay them.

```sh
# from the repository root
node companion/tools/gen-vectors.js          # regenerate parity vectors
dotnet run --project companion/tests/logic -- \
  companion/tests/vectors.json companion/sample-profile.json
dotnet build companion/tests/shipcheck       # C# 5 compile check of all shipped sources
node tests/run-tests.js                      # the studio's own core tests (unchanged)
```

CI (`.github/workflows/companion.yml`) builds the exe with the real
in-box compiler on a Windows runner and runs the logic tests.
