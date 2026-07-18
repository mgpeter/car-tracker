# API Specification

This is the API specification for the spec detailed in @docs/specs/2026-07-18-partial-fill-mpg/spec.md

No routes are added or removed. The change is to the **shape of the fuel summary** returned by the existing read
path, and to what the existing write paths accept for fill level (which they already accept — only the default
and semantics change client-side). All figures remain derived on read; nothing is stored.

## Affected endpoints

### GET /api/vehicles/{registration}/fuel

**Purpose:** Every fill with its computed MPG, plus fleet stats — now with per-fill grouping and the open-tank
state. Unchanged route, method and auth; the response body (`FuelEconomySummary`) gains fields.

**`FuelEconomySummary` — new fields:**

| Field | Type | Meaning |
|---|---|---|
| `pendingFillCount` | `int` | Trailing partial fills since the last full fill. `0` = tank closed (normal). |
| `pendingLitres` | `decimal` | Litres pumped into the open tank so far. `0` when `pendingFillCount == 0`. |
| `pendingMiles` | `int \| null` | Miles from the last full fill to the latest fill. `null` when there is no full-fill anchor yet. |

Existing fields (`averageMpg`, `perFillAverageMpg`, `bestMpg`, `worstMpg`, `totalLitres`, `totalCost`,
`averagePricePerLitre`, `lastFillDate`, `fillCount`, `measuredIntervalCount`, `implausibleCount`, `entries`) are
unchanged in name and type. `bestMpg`/`worstMpg`/`perFillAverageMpg` now range over grouped segment figures, and
`averageMpg` measures to the last closing fill — same fields, same types, better numbers.

**`FuelEntryMetrics` (each element of `entries`) — new fields:**

| Field | Type | Meaning |
|---|---|---|
| `segmentMiles` | `int \| null` | Distance the MPG figure covers. Equals `milesSinceLast` for an ungrouped fill; larger for a grouped one. `null` when there is no figure. |
| `spannedFillCount` | `int` | Fills the figure covers. `1` = ordinary, `≥2` = grouped a partial. `0` on a fill with no figure. |

`unreliableReason` gains the value `"AwaitingFullTank"` for a deferred partial fill (alongside the existing
`"NoPreviousFill"` and `"NonMonotonicMileage"`). `mpg` is `null` on such a row, as for any unmeasured fill.

**Response (illustrative, a Half fill followed by a Full fill):**

```jsonc
{
  "averageMpg": 29.2,
  "bestMpg": 32.2,
  "worstMpg": 25.4,
  "pendingFillCount": 0,
  "pendingLitres": 0,
  "pendingMiles": null,
  "entries": [
    // ... a Half fill: no figure, deferred
    {
      "fuelEntryId": 41, "fillLevel": "Half",
      "mpg": null, "isReliable": false, "unreliableReason": "AwaitingFullTank",
      "milesSinceLast": 95, "segmentMiles": null, "spannedFillCount": 0
    },
    // ... the Full fill that closes the tank: one figure over both fills
    {
      "fuelEntryId": 42, "fillLevel": "Full",
      "mpg": 30.1, "isReliable": true, "isPlausible": true, "unreliableReason": null,
      "milesSinceLast": 210, "segmentMiles": 305, "spannedFillCount": 2
    }
  ]
}
```

**Errors:** unchanged (404 on unknown registration).

### POST /api/vehicles/{registration}/fuel and PATCH /api/vehicles/{registration}/fuel/{id}

**No contract change.** `AddFillRequest` / `UpdateFillRequest` already carry an optional `FillLevel`
(Full/Half/Quarter). What changes is only its *meaning* to the derived figures the subsequent scan and read
produce, and the client default (POST from the sheet now sends `"Full"` unless the driver picks otherwise).

The XML doc comments on `AddFillRequest.FillLevel` and `AddFillSheet`'s field currently state fill level is
"descriptive only … does NOT gate MPG". That is no longer true and must be corrected: Full/unrecorded closes the
tank and measures MPG; Half/Quarter defers it. The validation is unchanged — fill level is still never a reason
to reject a save (§5.3); a partial fill is always accepted and simply defers its figure.

The `AddFillResponse.Flags` behaviour is unchanged, and improves: a partial fill no longer produces a spurious
`ImplausibleMpg` flag, because it produces no MPG for the band to judge.
