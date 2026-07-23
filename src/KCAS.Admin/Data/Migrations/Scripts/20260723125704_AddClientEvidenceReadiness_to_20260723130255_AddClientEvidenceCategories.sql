START TRANSACTION;
ALTER TABLE `Clients` ADD `ClientCategory` varchar(96) NOT NULL DEFAULT 'NaturalPerson';

CREATE INDEX `IX_Clients_ClientCategory` ON `Clients` (`ClientCategory`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260723130255_AddClientEvidenceCategories', '10.0.10');

COMMIT;

