START TRANSACTION;
ALTER TABLE `Clients` ADD `ExcludeFromComplianceLists` tinyint(1) NOT NULL DEFAULT FALSE;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260921160756_AddClientComplianceListVisibility', '10.0.10');

COMMIT;

