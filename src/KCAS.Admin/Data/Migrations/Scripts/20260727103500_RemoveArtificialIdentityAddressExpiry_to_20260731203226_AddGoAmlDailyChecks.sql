START TRANSACTION;
CREATE TABLE `GoAmlDailyChecks` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `CheckDate` date NOT NULL,
    `Status` varchar(32) NOT NULL,
    `StartedAtUtc` datetime(6) NOT NULL,
    `StartedBy` varchar(191) NOT NULL,
    `CompletedAtUtc` datetime(6) NULL,
    `CompletedBy` varchar(191) NULL,
    `Notes` longtext NULL,
    `MessageReference` varchar(512) NULL,
    `ActionOwner` varchar(191) NULL,
    `ActionDueDate` date NULL,
    `ComplianceTaskId` int NULL,
    `EvidenceFileName` varchar(255) NULL,
    `EvidencePath` varchar(1024) NULL,
    `EvidenceContentType` varchar(96) NULL,
    `EvidenceSizeBytes` bigint NULL,
    `EvidenceSha256` varchar(64) NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_GoAmlDailyChecks_ComplianceTasks_ComplianceTaskId` FOREIGN KEY (`ComplianceTaskId`) REFERENCES `ComplianceTasks` (`Id`) ON DELETE SET NULL
);

CREATE TABLE `GoAmlSettings` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `EvidenceRootPath` varchar(1024) NOT NULL,
    `PortalUrl` varchar(1024) NOT NULL,
    `TrackingStartDate` date NOT NULL,
    `DueHourLocal` int NOT NULL,
    `BackupChecker` varchar(191) NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    `UpdatedBy` varchar(191) NULL,
    PRIMARY KEY (`Id`)
);

CREATE UNIQUE INDEX `IX_GoAmlDailyChecks_CheckDate` ON `GoAmlDailyChecks` (`CheckDate`);

CREATE INDEX `IX_GoAmlDailyChecks_ComplianceTaskId` ON `GoAmlDailyChecks` (`ComplianceTaskId`);

CREATE INDEX `IX_GoAmlDailyChecks_Status_CheckDate` ON `GoAmlDailyChecks` (`Status`, `CheckDate`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260731203226_AddGoAmlDailyChecks', '10.0.10');

COMMIT;

