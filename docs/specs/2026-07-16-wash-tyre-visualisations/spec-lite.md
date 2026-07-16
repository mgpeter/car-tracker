# Spec Summary (Lite)

Give the wash and tyre screens the two bespoke visualisations their designs draw and the current screens omit: a
cadence bar showing where today sits against the 21–28 day wash window, and a CSS car-body corner diagram laying
out the asymmetric model — 5 pressures, 4 treads, the spare with a pressure but no tread target.

Both are presentation over figures that already exist (`WashPage.tsx` computes the gaps; `TyresPage.tsx` holds
the per-corner numbers), so there is no schema, no endpoint and no new arithmetic. Neither is an SVG — `Spark`
is the app's only hand-rolled SVG and earns it by plotting a series; a bar and four corner cards are CSS. The
wash pill flips OK→Overdue on the same `sinceLast > 28` rule the stat note already uses, but it does not join
the dashboard checks count — a wash is not a check, and conflating the axes is what this codebase avoids.
