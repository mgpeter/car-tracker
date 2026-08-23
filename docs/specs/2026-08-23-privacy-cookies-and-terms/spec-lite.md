# Spec Summary (Lite)

Publish a privacy policy, a cookie and storage notice and terms of use at `/privacy`, `/cookies` and `/terms`,
reachable by a signed-out visitor, with the controller supplied by a `Legal:` configuration section so a
self-hosted deployment names its own operator or publishes nothing at all. No consent banner: the app sets no
cookies and stores nothing non-essential, so the notice lists the five client-side keys instead - sourced from
a registry the code imports rather than from prose. Sign-up carries the links and stamps
`users.terms_version`, and the retention the policy states is enforced by a service that prunes the three
operational ledgers.
