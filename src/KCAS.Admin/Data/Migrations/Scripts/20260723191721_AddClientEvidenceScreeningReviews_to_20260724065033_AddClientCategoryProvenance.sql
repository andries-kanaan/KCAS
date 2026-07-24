START TRANSACTION;
ALTER TABLE `Clients` ADD `ClientCategoryReason` varchar(512) NULL;

ALTER TABLE `Clients` ADD `ClientCategorySource` varchar(32) NOT NULL DEFAULT 'Unknown';

ALTER TABLE `Clients` ADD `ClientCategoryUpdatedAtUtc` datetime(6) NULL;

ALTER TABLE `Clients` ADD `ClientCategoryUpdatedBy` varchar(191) NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260724065033_AddClientCategoryProvenance', '10.0.10');

COMMIT;

