# Design memo: the native injection layer (future work)

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
- **Focus**: injected keys go to the focused window. A "target app"
  concept (only fire when a chosen app is focused) belongs in the shell,
  not in profiles.
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
