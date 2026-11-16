# Privacy

Hands is local-first software.

- **No server.** Hands has no backend and no account system.
- **No network.** The app is a single HTML file whose Content Security
  Policy sets `connect-src 'none'`; it cannot send data anywhere.
- **No analytics, no telemetry.** Nothing about your usage is collected
  or reported.
- **Your data stays on your device.** Profiles (your button bindings)
  are stored only in your browser's local storage on the device you use.
  Exporting a profile produces a JSON file that you control; Hands never
  sees it leave.
- **Controller input is read, never recorded.** Gamepad state is polled
  in memory to detect combinations and is not logged or persisted. The
  on-screen output log exists only until you clear it or close the page.

If a future native companion is built (see `docs/INJECTION.md`), the same
rules apply to it: profiles are files the user owns, and the creator of
this software holds no user data.
