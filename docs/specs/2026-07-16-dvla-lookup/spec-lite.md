# Spec Summary (Lite)

A server-side `GET /api/vehicles/lookup/{reg}` calls the DVLA VES and DVSA MOT History APIs and returns
un-persisted facts (make, model, year, colour, engine, fuel, MOT and tax status) to pre-fill the add-car sheet —
the plate-input + "Look up" flow `garage.dc.html` shows, with its promise "you confirm before anything is
created". The lookup fills the form; `VehicleFactory` still creates the car only on submit.

The one hard design point: the DVLA's MOT expiry seeds `MotExpirySeed` (or an initial MOT `ServiceRecord`),
never a stored MOT-expiry countdown — a derived figure from a logged pass must always win, because a stored MOT
expiry is the first of the five defects this project exists to fix. Keys stay server-side (CSP blocks a
browser→DVLA call regardless); an un-resolvable reg falls back cleanly to manual entry.
