START TRANSACTION;
ALTER TABLE station_transactions ADD "Simulated" boolean NOT NULL DEFAULT FALSE;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261126014651_AddSimulatedStationTransaction', '10.0.14');

COMMIT;

