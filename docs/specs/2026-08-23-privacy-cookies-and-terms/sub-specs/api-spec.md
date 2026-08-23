# API Specification

This is the API specification for the spec detailed in
@docs/specs/2026-08-23-privacy-cookies-and-terms/spec.md

**No new endpoint.** The three documents are client-side components in the SPA bundle, served by the gateway's
existing SPA fallback like every other route; nothing about them is fetched. What the API gains is one nullable
block on a response that already exists, and one field on the export.

The diff is **additive**: no existing path changes, no existing field changes type, and no enum gains a member.

## `GET /api/meta` - gains a nullable `legal` block

Anonymous, as it already is, and that is the right home for this: the controller's identity is a statement
about the *deployment*, published to strangers by definition. It is the same cache entry `Footer` and
`AuthGate` already fill, so the legal pages cost no extra request.

**Response, added last:**

```jsonc
{
  "applicationName": "CarTracker",
  "version": "0.28.0",
  // ... the four existing capability flags, unchanged ...
  "legal": {
    "controllerName": "…",
    "controllerContact": "…",
    "controllerAddress": "…",   // nullable
    "jurisdiction": "United Kingdom",
    "hostingSummary": "…"       // nullable
  }
}
```

**`legal` is null when the deployment publishes no documents**, which is any deployment leaving
`Legal:ControllerName` or `Legal:ControllerContact` blank. Null is the whole signal: the client renders no
footer links, and the three routes fall through to the landing page.

**Declared as `LegalInfo? Legal = null`, appended last to `MetaResponse`.** `MetaEndpoints.cs:114-120` records
that a defaulted record parameter emits as nullable in the generated contract and that this broke CI when
nullable was *not* wanted. Here it is wanted, so the same mechanism is used deliberately rather than worked
around, and the note in that file gains a sentence saying so - otherwise the next reader applies the lesson
backwards.

**The client reads it as `!= null`**, the hide-when-absent polarity of `chatConfigured`,
`vehicleLookupConfigured` and `identityDeletionConfigured`, and explicitly *not* `signupInviteOnly`'s. An
in-flight `meta` therefore renders no legal links and no acceptance line rather than a link to a page that will
not exist. The three routes render a splash while `meta` is pending, then either the document or a redirect to
`/`.

**No credential is disclosed.** Every field is prose the deployment's operator chose to publish. The two
capability flags the documents branch on (`chatConfigured`, `vehicleLookupConfigured`) are already on this
response and already anonymous, for the reason recorded there: they say what a capability is, not what a
credential is.

## `GET /api/account/export` - gains `termsVersion`

One field on the export's existing `account` provenance block:

```jsonc
"account": {
  "email": "…",
  "createdAt": "2026-03-14T…",
  "termsVersion": "2026-08-23"   // nullable
}
```

**It is a stored row, so the export's rule permits it.** The rule is that no *derived* figure travels in an
archive, because a stored derived value in a file read when nothing can recompute is the workbook's five
defects in a new costume. `terms_version` is an observation stamped once at provisioning and recomputed by
nothing, so it belongs in an archive of stored rows exactly as `createdAt` does.

**The importer does not write it.** It sits in the `account` block, which is provenance shown in the preview
and written nowhere - joining `email` and `createdAt`, which are already read and already discarded. Importing
it would assert that the receiving account accepted a document at a time and on a deployment that has nothing
to do with it.

## Errors

None are added. The three legal paths are SPA routes, so an unconfigured deployment answers them with
`index.html` and a 200 exactly as it answers `/bt53akj/fuel`, and the decision about what to render is the
client's. That is the same `MapFallbackToFile` behaviour that argues in the technical spec for committing the
documents as components rather than as fetched files: a 200 for a path the server knows nothing about is
precisely why nothing here may depend on a fetch succeeding.

## Not changed

- **No MCP tool.** The catalogue is about a vehicle's records; a deployment's privacy policy is not one, and
  widening the assistant's surface is a separate decision every time.
- **No admin endpoint.** The controller details are configuration, and `/admin`'s configuration panel already
  reports what the container resolved - it gains the `Legal:` posture as a row on that panel, through the read
  service that is already there, and no new path.
- **`GET /api/meta/authenticated` is untouched.** Whether documents are published is a fact about the
  deployment, not about an account, and that endpoint's own comment draws exactly that line.
