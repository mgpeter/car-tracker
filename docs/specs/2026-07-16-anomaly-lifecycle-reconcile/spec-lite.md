# Spec Summary (Lite)

Close an open integrity flag automatically when the data behind it is deleted or edited away, so the queue
reflects what is wrong now rather than what was once wrong. The detector is pure and additive — it only ever
raises flags — so a flag whose cause disappears currently stays Open forever, pointing at a row that no longer
exists (found live this session, resolved by hand).

After each scan, an Open flag whose condition is no longer detected is marked Corrected with a system note and
its row kept; Accepted and Dismissed flags are never touched, because they are the owner's decisions. Reusing
Corrected means a recreated condition re-raises, which falls out of the existing re-raise rule rather than
needing new machinery.
