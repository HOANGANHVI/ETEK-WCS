START TRANSACTION;
DO $$ BEGIN
  IF EXISTS (SELECT 1 FROM transport_orders WHERE "RacksHeld") THEN
    RAISE EXCEPTION 'ROLLBACK_REFUSED: failed orders still hold racks; release them first.';
  END IF;
END $$;

ALTER TABLE transport_orders DROP COLUMN "RacksHeld";

DELETE FROM "__EFMigrationsHistory"
WHERE "MigrationId" = '20261126072813_AddOrderRacksHeld';

COMMIT;

START TRANSACTION;
DO $$ BEGIN
  IF EXISTS (SELECT 1 FROM transport_orders o JOIN business_process_versions v ON v."Id" = o."ProcessVersionId"
             WHERE v."WithdrawnByAdmin" AND o."QueueSequence" IS NOT NULL AND o."LegCount" = 0 AND o."State" NOT IN ('Completed', 'Failed', 'Cancelled')) THEN
    RAISE EXCEPTION 'ROLLBACK_REFUSED: queued requests are pinned to a version an Admin disabled; cancel them first.';
  END IF;
END $$;

ALTER TABLE business_process_versions DROP COLUMN "WithdrawnByAdmin";

DELETE FROM "__EFMigrationsHistory"
WHERE "MigrationId" = '20261126031415_AddVersionWithdrawnByAdmin';

COMMIT;

