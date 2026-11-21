# Design memo: the native injection layer

## Status (updated)

**Windows: minimal version implemented** in
[`companion/`](../companion/README.md) — XInput polling in, `SendInput`
with scan codes out, profiles validated with this memo's rules, built by
the C# compiler that ships with Windows (no installs). The detection
engine is a port of HANDS CORE whose parity is proven by generated test
vectors, not by eyeballing. It is **not yet verified on a real Windows
machine**; the check procedure lives in the companion's README.
Linux and macOS remain design-only, as described below.

Deviations and limits of the minimal version, recorded honestly:

- Input is XInput only (no Raw Input / HID path yet), so the pad is
  read with the standard-mapping layout; the Guide button (index 16)
  cannot be read through `XInputGetState` and is never reported.
- The studio's keyboard test buttons (`k0`–`k7`) are a browser-only
  fallback and are not fed by the companion.
- Target-app filtering is implemented: a profile may carry an optional
  `target` (`titleContains` and/or `processName`; both together mean
  AND). The companion checks the foreground window before each send,
  skips the send when it does not match, and logs the reason — a skipped
  send is surfaced, never silently dropped. With no `target`, keys go
  to the focused window exactly as before. A refused send *is* also
  surfaced, per the pitfall below — the log names the foreground
  window's title when `SendInput` accepts fewer events than requested.
- The first-run injection test exists as an explicit
  `--selftest-send` flag (one harmless Shift tap) rather than an
  automatic first-run flow.
- A panic chord (Ctrl+Alt+F12), Ctrl+C, or closing the window stops it.
- Punctuation keys follow the US layout (the studio stores produced
  characters; scan codes are layout territory, as noted below).

Hands (the web app in this repository) is the **configuration studio**: it
reads real controller input, lets you design combo bindings, verifies them
against the deterministic detection engine, and stores profiles as portable
JSON. What it deliberately does **not** do is deliver keys to other
applications — a web page cannot inject input into another app, and this
memo records how a future native build would close that gap without
changing the profile format or the detection semantics.

## Why the web layer cannot inject

- Browsers sandbox pages: synthetic `KeyboardEvent`s dispatched by a page
  are marked untrusted and never reach other applications or even other
  browser windows.
- The Gamepad API is read-only by design.
- OS-level input injection requires native facilities (and permissions)
  that the web platform intentionally does not expose.

## The plan: a thin native shell, same core

The detection engine (`HANDS CORE` in `index.html`) is pure and
clock-free: it consumes timestamped button events and emits fired
bindings. A native shell only has to provide three things:

1. **Input source** — read controllers below the browser:
   - Windows: XInput / Raw Input (HID) for pads, including XR controllers
     exposed as gamepads.
   - Linux: evdev (`/dev/input`) or SDL2's gamepad layer.
   - macOS: IOKit HID / GameController framework.
   Each platform adapter translates device buttons to Hands button ids
   (`g{pad}b{index}`) and feeds `handleEvent` / `advanceTo` exactly like
   the web polling loop does.

2. **Output sink** — turn fired actions into OS input events:
   - Windows: `SendInput` with scan codes (KEYEVENTF_SCANCODE) so games
     that read raw input accept the keys.
   - Linux: a virtual keyboard via `uinput` (`/dev/uinput`), which works
     for both X11 and Wayland sessions (unlike XTest, which Wayland
     compositors ignore).
   - macOS: `CGEventPost` (requires the Accessibility permission; the app
     must ask for it explicitly and fail loudly without it).

3. **Profile loading** — read the same JSON the studio exports
   (`{"app":"hands","version":1,"profile":{...}}`). No format change:
   the studio stays the single authoring tool, the shell is a player.

## Known platform pitfalls (recorded so they are not rediscovered)

- **Anti-cheat / protected games** may block synthetic input entirely.
  The shell must surface "injection refused" per target instead of
  silently dropping keys — failing loudly is a product rule.
- **Focus**: injected keys go to the focused window. The "target app"
  concept (only send when a chosen app is focused) is enforced in the
  shell, as planned here; the profile merely *declares* the target (the
  optional `target` field the studio authors), and the shell performs
  the foreground check and owns the skip-and-log decision. The check
  reads only the foreground window's title and owning process name —
  the same information the sender already logged for refused sends.
- **Permissions**: macOS Accessibility and Linux uinput group membership
  are install-time hurdles; the shell's first-run flow must test
  injection with a harmless key and show the result.
- **Latency**: the engine is O(rules) per event and allocation-light;
  a 1 kHz poll is unnecessary — matching the device's report rate
  (typically 60–250 Hz) keeps added latency below one frame.
- **Security**: profiles are data, never code. The shell must treat a
  profile file as untrusted input and validate it with the same
  normalization rules as the core (`normalizeProfile`), rejecting
  anything else.

## Non-goals for the shell

- No cloud sync of profiles (files are the sync mechanism; the creator
  holds no user data — a standing rule for all of these products).
- No scripting inside profiles. Macros are fixed key sequences with
  delays, nothing more.
- No reading of other apps' state. The shell writes input; it does not
  observe the screen or window contents beyond focus checks the user
  explicitly enables.
