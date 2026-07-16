# Spec Summary (Lite)

A `Trip`/outing log — date, place, terrain, difficulty (1–3), an optional end odometer — that on save writes a
`MileageReading` and **prompts** the underside rinse (wash reset) and the coolant/oil recheck the green-lane
field manual prescribes after every outing. It closes the loop between driving BT53 off-road and the maintenance
off-road use then demands: the K-series head gasket and the VCU are exactly what green-laning stresses, and
every route card in `archive/…green-lane-field-manual.html` ends in "rinse underside, recheck coolant".

This is **net-new, speculative scope outside README §1–§8**, drawn from the app's own visual-identity source and
larger than a normal feature — it should earn an explicit DEC before implementation. Recommended v1 is the
outing log alone (reusing mileage, wash cadence and the coolant check); the route reference (the planner half)
and the Leaflet map (CSP-blocked as designed) are deferred as larger and more speculative. Live TRO/Trailwise
legal-status integration is firmly out — a wrong "legal today" is a legal risk, not a convenience.
