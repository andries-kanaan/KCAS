START TRANSACTION;
ALTER TABLE `ClientPersonalProfiles` ADD `PassportCountry` varchar(96) NULL;

ALTER TABLE `ClientPersonalProfiles` ADD `PassportExpiryDate` date NULL;

ALTER TABLE `ClientPersonalProfiles` ADD `PassportNumber` varchar(64) NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261004154805_AddClientPassportDetails', '10.0.10');

COMMIT;

