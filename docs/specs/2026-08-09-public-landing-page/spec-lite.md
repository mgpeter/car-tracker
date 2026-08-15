# Spec Summary (Lite)

Replace the login wall's bare splash - a heading, one sentence and two buttons - with a one-page welcome that
says what Car Tracker is, shows real screens, and invites a visitor to sign up or log in. It renders in place
of `AuthGate`'s signed-out branch, **above the router**, so the security boundary that stops any screen
rendering before Auth0 confirms a session is untouched. The cost, recorded rather than hidden: the page has no
URL of its own.

The calls to action already exist (`loginWithRedirect`, and `screen_hint: 'signup'` for the second); what is
missing is everything around them. The copy is assembled from what the repo already says well - the README's
account of a spreadsheet whose Dashboard stores five provably wrong figures, and the mission's two
differentiators - rather than invented. Screenshots move from `docs/images/` (not served, and excluded by
`.dockerignore`) into `src/assets/screens/`, downscaled to WebP so Vite fingerprints them; they will be the
app's first bundled raster assets.

Built from real CSS classes on the `.g-hero` pattern, not inline styles, so `tokens.test.ts`'s colour-literal
guard still applies. The known trap: `.btn` is `--fg` on `--bg` and goes near-invisible on the dark hero band
in light theme.

No schema, no endpoint, no contract change, and no change to the auth flow. The three public-release gates -
per-user reference tables, HTTPS, and DEC-016's first-user-claims-unowned-vehicles - are recorded on the
roadmap by this work and fixed by none of it.
