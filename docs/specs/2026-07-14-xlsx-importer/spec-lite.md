# Spec Summary (Lite)

Import the existing 13-sheet workbook so no history is retyped, reading from the 12 log sheets and recomputing rather than trusting the Dashboard's stored values. The Dashboard is never an input — it is a test fixture, and the four figures where it disagrees with the logs (MOT expiry, total litres, fuel YTD, current mileage) become regression cases proving the recompute. Bad data imports as written with an anomaly flag attached, per README §5.3: the 83,000 mi service row is neither silently corrected nor allowed to reject the import.
