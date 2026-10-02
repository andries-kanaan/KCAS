START TRANSACTION;
ALTER TABLE `EmployeeProfiles` ADD `TransferKey` varchar(64) NOT NULL DEFAULT '';

UPDATE `EmployeeProfiles` SET `TransferKey` = REPLACE(UUID(), '-', '') WHERE `TransferKey` = '';

CREATE TABLE `EmployeeTransferRecords` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `PackageId` varchar(64) NOT NULL,
    `Direction` varchar(16) NOT NULL,
    `EmployeeKey` varchar(64) NOT NULL,
    `SourceSystemKey` varchar(64) NOT NULL,
    `EmployeeProfileId` int NOT NULL,
    `SourceDigest` varchar(64) NOT NULL,
    `LocalDigest` varchar(64) NOT NULL,
    `MappingJson` longtext NOT NULL,
    `ActorNamesJson` longtext NOT NULL,
    `StoragePath` varchar(1024) NOT NULL,
    `FileName` varchar(191) NOT NULL,
    `UserId` varchar(64) NOT NULL,
    `Reason` longtext NOT NULL,
    `PackageCreatedAtUtc` datetime(6) NOT NULL,
    `RecordedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_EmployeeTransferRecords_EmployeeProfiles_EmployeeProfileId` FOREIGN KEY (`EmployeeProfileId`) REFERENCES `EmployeeProfiles` (`Id`) ON DELETE RESTRICT
);

CREATE UNIQUE INDEX `IX_EmployeeProfiles_TransferKey` ON `EmployeeProfiles` (`TransferKey`);

CREATE INDEX `IX_EmployeeTransferRecords_EmployeeProfileId` ON `EmployeeTransferRecords` (`EmployeeProfileId`);

CREATE UNIQUE INDEX `IX_EmployeeTransferRecords_PackageId_Direction_EmployeeKey` ON `EmployeeTransferRecords` (`PackageId`, `Direction`, `EmployeeKey`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261002132242_AddEmployeeTransfers', '10.0.10');

COMMIT;
