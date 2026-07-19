# Spec Summary (Lite)

When adding a car with the generic starter set, present its fifteen checks as an inline toggle list (defaulted
all-on, with an "N of 15" count) and create only the kept ones — so the founding check set matches the car
instead of being an all-or-nothing fifteen. The template is exposed by a read endpoint (server stays the single
source of truth), the create request carries the selected check names, and `VehicleFactory` filters the generic
template to that subset. Touch nothing and the result is byte-for-byte today's fifteen; deselect all and it
equals choosing "None". Editing cadence, adding custom checks, and copy-from-vehicle are out of scope.
