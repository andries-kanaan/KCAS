START TRANSACTION;
CREATE TABLE `GoAmlTransferRecords` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `PackageId` varchar(36) NOT NULL,
    `Direction` varchar(16) NOT NULL,
    `ContentSha256` varchar(64) NOT NULL,
    `Status` varchar(32) NOT NULL,
    `FileName` varchar(260) NOT NULL,
    `StoragePath` varchar(512) NOT NULL,
    `FirstCheckDate` date NOT NULL,
    `LastCheckDate` date NOT NULL,
    `CheckCount` int NOT NULL,
    `SummaryJson` longtext NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `AppliedAtUtc` datetime(6) NULL,
    `AppliedBy` varchar(191) NULL,
    PRIMARY KEY (`Id`)
);

CREATE INDEX `IX_GoAmlTransferRecords_Direction_ContentSha256` ON `GoAmlTransferRecords` (`Direction`, `ContentSha256`);

CREATE INDEX `IX_GoAmlTransferRecords_Direction_CreatedAtUtc` ON `GoAmlTransferRecords` (`Direction`, `CreatedAtUtc`);

CREATE UNIQUE INDEX `IX_GoAmlTransferRecords_Direction_PackageId` ON `GoAmlTransferRecords` (`Direction`, `PackageId`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260801093743_AddGoAmlTransfers', '10.0.10');

COMMIT;
