# Spec Summary (Lite)

Make every add/edit sheet fast and forgiving: render the server's already-emitted per-field validation `errors`
inline (red outline + plain message + `aria-invalid`) instead of a generic "Bad Request" banner, backed by
lightweight client-side required checks. Default record dates to today with "+6 months"/"+1 year" quick-fill
links on forward-looking dates, and replace free-text place fields (garage, station, wash location, vendor,
tool, …) with a custom accessible combobox that shows recent/known values yet still accepts new ones — sourcing
garages/wash-locations from their reference GETs and the rest from the vehicle's own history. No schema or
endpoint changes.
