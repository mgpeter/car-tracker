# Spec Summary (Lite)

Read an account export back in: upload the JSON, see exactly what it would do, then commit it, and the file's
garage and cars are cloned into the signed-in account alongside whatever is already there, under new ids.

**The rows are inserted, not replayed.** Running the file through the write paths would fire the four expense
mirrors a second time against rows the file already contains - a fill would write its own mirrored expense and
its own mileage reading, both of which are in the file. So the import writes rows and remaps foreign keys
through an id map, and asserts the invariants the factories would have enforced instead of re-deriving them.

**A registration you already own is imported under a modified one** (`BT53 AKJ` becomes `BT53 AKJ-2`),
proposed by the server, shown in the preview and editable before anything is written. The cost is stated
rather than hidden: the plate becomes fictional, so the imported vehicle's notes record the registration it
was cloned from and the date it arrived.

**Four things in the file are deliberately not imported.** Document rows, because the export carries no bytes
and a row pointing at a missing file is the failure a restore-without-documents produces. Assistant tokens,
because a token without its secret is not a credential. The write-audit trail, because it describes writes
that happened on another deployment. And anomaly flags, which are re-derived by `AnomalyScanner` after the
rows land, so the integrity queue describes this database rather than another one.

**Reference lists merge by name and never overwrite.** A garage whose name the account already holds is
matched, not updated - letting an imported file rewrite your own garage's address is the shape of the
cross-tenant write DEC-018 closed, self-inflicted.

**No schema change and no migration.** `EntrySource.Import` already exists in the enum and its check
constraint, left behind when DEC-008 deleted the importer, so imported rows can say what they are for free.
The pending preview lives in `IMemoryCache` like the chat's `PendingWriteStore`, because losing one to a
container restart costs a re-upload rather than an allowance.

**The headline test is a round trip**: export, import into a second account, export that, and compare. The two
payloads must be equal except for ids, timestamps and the account block. It is the only test that proves the
import understood every table rather than the ones someone remembered to assert on.
