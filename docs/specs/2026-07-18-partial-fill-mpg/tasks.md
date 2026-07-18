# Spec Tasks

- [ ] 1. Tank-to-tank grouping in the domain
  - [ ] 1.1 Write tests for `FuelEconomyCalculator`: a Half fill defers (`AwaitingFullTank`, no MPG); a Full
        after a partial computes over summed litres and the full span with `SpannedFillCount == 2`; a `null`
        fill level closes the tank identically to `Full`; Half and Quarter behave identically (magnitude
        unread); all existing all-`Full` cases stay green.
  - [ ] 1.2 Add `AwaitingFullTank = 4` to `MpgUnreliableReason`; add `SegmentMiles` and `SpannedFillCount` to
        `FuelEntryMetrics`; add `PendingFillCount`, `PendingLitres`, `PendingMiles` to `FuelEconomySummary`.
        Correct the now-false "descriptive only / nothing depends on it" doc comments.
  - [ ] 1.3 Add the `ClosesTank` helper and replace the pairwise `Measure` loop with the open-segment walk;
        populate the new per-entry and summary fields; refine cumulative `AverageMpg` to measure to the last
        closing fill.
  - [ ] 1.4 Replace `NoFillLevelInCalculationsTests` with a test pinning the new contract (only Full/null
        closes; Half/Quarter defers; magnitude unread). Verify the workbook fixture (`AverageMpg` 29.19,
        Best/Worst, per-fill figures) is unchanged.
  - [ ] 1.5 Verify all domain tests pass.

- [ ] 2. Anomaly wording and false-flag removal
  - [ ] 2.1 Write tests: a partial fill raises no `ImplausibleMpg`; a genuinely off-band *grouped* figure still
        does, with `SegmentMiles` in the message.
  - [ ] 2.2 Update `AnomalyDetector.DetectImplausibleMpg` message/detail to use `SegmentMiles` (and note a
        multi-fill span when `SpannedFillCount > 1`). No detection-logic change — it already reads the
        calculator.
  - [ ] 2.3 Verify anomaly tests pass.

- [ ] 3. API contract and typed client
  - [ ] 3.1 Confirm the new `FuelEconomySummary` / `FuelEntryMetrics` fields serialise as specified; correct the
        `AddFillRequest.FillLevel` doc comment (no longer "descriptive only").
  - [ ] 3.2 Regenerate the committed OpenAPI contract and the typed front-end client off it.
  - [ ] 3.3 Verify the WebApi tests pass and the contract diff is only additive.

- [ ] 4. Front-end labels and pending state
  - [ ] 4.1 Write/extend `FuelLogPage.test.tsx`: the pending row shows "MPG pending · next full fill"; a grouped
        figure shows the "over N fills · M mi" sub-label; the dashboard panel shows "part-tank in progress" when
        `pendingFillCount > 0`.
  - [ ] 4.2 `FuelTable.tsx`: add the `AwaitingFullTank` entry to `NO_MPG`, render the grouped sub-label from
        `spannedFillCount`/`segmentMiles`, update the "Fill" column header note.
  - [ ] 4.3 `AddFillSheet.tsx`: default the Fill level to `Full` on a new fill; update the field hint; make the
        live MPG preview show "pending next full fill" when Half/Quarter is selected.
  - [ ] 4.4 `FuelPanel.tsx` (dashboard): render the calm "part-tank in progress" line when a tank is open.
  - [ ] 4.5 Verify all front-end tests pass.

- [ ] 5. Dogfood the real partial fill
  - [ ] 5.1 Enter BT53's actual Half fill from today through the add-fill sheet; confirm it defers, the summary
        shows the part-tank in progress, and no flag is raised.
  - [ ] 5.2 Confirm the next Full fill (when it happens) posts one grouped figure and clears the pending state —
        or add a follow-up note that the open tank is expected until then.
