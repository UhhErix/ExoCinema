# Exo Cinema security

## Reporting a vulnerability

Report security problems privately: on GitHub, **Security > Report a vulnerability** in this repository. Don't open a public issue for them. Say what you found, how to reproduce it and which version you used (**Settings > About**). You'll get an answer, and a fix ships in the next release with a line in the changelog.

## How Exo Cinema is put together

- **One process, your rights.** `ExoCinema.exe` runs as your Windows user (`asInvoker`) and installs per user; nothing runs as administrator or as a service.
- **The interface is Exo Cinema's own page.** It is served from the app folder (`app\wwwroot`) under its own address in WebView2, not from the web. It reaches your server only through the app: the page asks, and the app makes the request with your sign-in.
- **Your sign-in.** Exo Cinema signs in with your user name and password once and keeps only the access token your server gives it, sealed with Windows' data protection (DPAPI) for your Windows user. The password is never stored.
- **The player.** Video is decoded and drawn by libmpv in the app's own process, with hardware decoding where the graphics card offers it. Its key bindings and on-screen controller are off and it reads no configuration files: it plays only what Exo Cinema gives it.
- **Updates** come from Exo's update address (`updates.getexo.app`), which serves Exo's copy of this repository's releases, or from the releases here on GitHub when it doesn't answer. A downloaded Setup must come from one of those hosts, match the SHA-256 published with the release and carry Exo Cinema Setup's version details before it runs. Setup stages the new version beside the old one and swaps it in whole, putting the old one back if anything fails.
