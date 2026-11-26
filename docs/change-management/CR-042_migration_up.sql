START TRANSACTION;
ALTER TABLE business_process_versions ADD "WithdrawnByAdmin" boolean NOT NULL DEFAULT FALSE;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261126031415_AddVersionWithdrawnByAdmin', '10.0.14');

COMMIT;

START TRANSACTION;
ALTER TABLE transport_orders ADD "RacksHeld" boolean NOT NULL DEFAULT FALSE;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261126072813_AddOrderRacksHeld', '10.0.14');

COMMIT;

