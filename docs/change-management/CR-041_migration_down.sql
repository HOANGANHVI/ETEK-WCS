START TRANSACTION;
DO $$ BEGIN
  IF EXISTS (SELECT 1 FROM station_transactions WHERE "Simulated" AND "OrderId" IS NULL AND "State" = 'Accepted') THEN
    RAISE EXCEPTION 'ROLLBACK_REFUSED: simulated requests are still waiting to start; resolve them first.';
  END IF;
END $$;

ALTER TABLE station_transactions DROP COLUMN "Simulated";

DELETE FROM "__EFMigrationsHistory"
WHERE "MigrationId" = '20261126014651_AddSimulatedStationTransaction';

COMMIT;

