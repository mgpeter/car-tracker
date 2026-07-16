# Spec Tasks

## Tasks

- [ ] 1. Record the decision, then the `Trip` entity
  - [ ] 1.1 Write a DEC: this is net-new scope outside README §1–§8; confirm v1 = the outing log (a) only,
        `MileageOrigin.Trip` vs reuse `Manual`, and whether a trip's mileage reading is kept or cascaded on delete
  - [ ] 1.2 Write tests for the `Trip` config: vehicle-scoped, difficulty CHECK 1–3, cascade on vehicle delete
  - [ ] 1.3 `Trip` entity + `TripConfiguration` (explicit column types), and the `MileageOrigin.Trip` value if chosen
  - [ ] 1.4 Migration `AddTrips` (table, index, CHECK, additive enum value); no backfill
  - [ ] 1.5 Verify tests pass

- [ ] 2. The outing write and its mileage reading
  - [ ] 2.1 Write tests: creating a trip with an end odometer writes one `MileageReading` (correct origin/date);
        no odometer writes none; a below-current odometer raises the same anomaly the other paths raise
  - [ ] 2.2 `POST /api/trips` + `GET /api/trips` (per vehicle) + edit/delete, following the log-endpoint pattern;
        the create writes the reading and runs the `AnomalyScanner` in one path
  - [ ] 2.3 Regenerate the OpenAPI contract and TS types; staleness gate green
  - [ ] 2.4 Verify tests pass

- [ ] 3. The maintenance loop — prompts, not writes
  - [ ] 3.1 Write tests: saving a trip surfaces the wash-reset and coolant-recheck prompts but logs NO
        `WashEntry` and NO `CheckLog` until the owner confirms
  - [ ] 3.2 On save, surface the "rinse underside + arches" wash reset and the coolant/oil recheck (the K-series
        head-gasket checks), deep-linking to the normal wash and check-log paths; degrade to advice if the
        check definitions are absent
  - [ ] 3.3 Verify tests pass

- [ ] 4. The Trips screen
  - [ ] 4.1 Write tests: the outing list shows difficulty/terrain/miles; the add sheet round-trips and surfaces
        the prompts; a reload preserves outings
  - [ ] 4.2 `TripsPage.tsx` + add-outing sheet, reusing the green-lane design language (difficulty bars, aftercare
        footer) onto the existing tokens; status axis kept separate from the orange accent
  - [ ] 4.3 Axe sweep + coverage-guard exemptions
  - [ ] 4.4 Verify tests pass

- [ ] 5. Prove it end to end on BT53
  - [ ] 5.1 Log a plausible Surrey byway outing (date, place, terrain, difficulty, end odometer); confirm the
        `Trip`, the `MileageReading` in the mileage log, and the moved current mileage
  - [ ] 5.2 Confirm the wash-reset and coolant-recheck prompts appear and log nothing until confirmed; confirm
        them and see the `WashEntry` and coolant `CheckLog` land through the normal paths
  - [ ] 5.3 Full suite, both builds, codegen gate; confirm the DEC is recorded; update roadmap/CLAUDE.md
