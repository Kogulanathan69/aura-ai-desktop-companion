# Step 13J — audit outbox delivery constraint hardening

The Step 13G `CK_SecurityAuditOutbox_Delivery` check allowed a manually written `Failed` row with nonnull `DeliveredAt`. Step 13H never writes that shape, but the database did not enforce the intended invariant. Migration `20261014000004_HardenSecurityAuditOutboxDelivery` replaces only this named check constraint. It leaves all columns, rows, indexes, other constraints, and other tables unchanged. Its `Down` restores the original Step 13G expression exactly; the historical migration is untouched.

The new allowed shapes are exactly:

| State | AttemptCount | DeliveredAt |
| --- | --- | --- |
| Pending | 0 | NULL |
| Delivered | > 0 | NOT NULL |
| Failed | > 0 | NULL |

Before applying the migration to any database, inspect existing rows for the shape the old constraint allowed. In particular, this query must return zero rows:

```sql
SELECT COUNT(*)
FROM "SecurityAuditOutbox"
WHERE "DeliveryState" = 2 AND "DeliveredAt" IS NOT NULL;
```

The migration performs no automatic data cleanup or repair. A nonzero result is a blocker requiring separate review; the new check would reject those rows. The migration was **not applied** on this PC, and live PostgreSQL verification was **not run**. The repository's missing historical baseline migration remains a separate deployment and test setup concern. The Step 13I disposable database harness should be run against a reviewed baseline before production consideration.

The production dispatcher remains unregistered. No hosted worker, automatic retry, API change, or readiness change is part of this step. Exactly-once delivery is not claimed; an external side effect may occur before an outbox transaction rolls back, so external duplication risk remains. A later disposable database test should confirm migration application, rejection of invalid Failed rows, rollback, and the Step 13I delivery scenarios.
