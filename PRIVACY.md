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
- **Share links carry the profile in the link itself.** When you create
  a share link, the profile is encoded in the URL fragment (the part
  after `#`), which browsers never send to a server — and Hands has no
  server to send it to. Anyone who has the link can read the profile it
  carries, so share links only with people you want to have that
  profile. Opening a share link imports the profile on that device;
  nothing about it is reported back.
- **Controller input is read, never recorded.** Gamepad state is polled
  in memory to detect combinations and is not logged or persisted. The
  on-screen output log exists only until you clear it or close the page.

The native Windows companion in `companion/` (see `docs/INJECTION.md`)
follows the same rules: profiles are files the user owns, it makes no
network requests, and the creator of this software holds no user data.
Raw controller state is never recorded there either; its local log
contains only the bindings that fired, and the file sits next to the
program on your own device, yours to delete.
