# Spec Summary (Lite)

Build the real trend charts README §8 defers — MPG over time, fuel price over time, and cumulative spend over
time by category — which the static `Spark` sparkline explicitly does not discharge (its own comment says so).
Every chart is a derived view over data already exposed: `FuelEntryMetrics` per fill and the expense log by date
and category; nothing new is stored, and a cumulative-spend chart's final point must equal the spend headline
because both read the same expenses.

The recommendation is to **extend the hand-rolled SVG approach `Spark.tsx` established, not add a chart library**:
the app self-hosts under a strict CSP, values a small dependency surface, and `Spark` already solved the two hard
parts a library reintroduces — a *derived* accessible name (not a colour-only legend) and greyscale-legible
markers. Interactivity (zoom, pan, tooltips) is a nice-to-have layered on later; v1 is readable and accessible
first.
