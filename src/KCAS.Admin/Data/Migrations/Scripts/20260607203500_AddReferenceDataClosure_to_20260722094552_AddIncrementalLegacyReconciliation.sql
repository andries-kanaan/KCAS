START TRANSACTION;
ALTER TABLE `Clients` ADD `LegacyReconciliationStatus` varchar(32) NOT NULL DEFAULT 'Unscanned';

CREATE TABLE `LegacyImportRuns` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `Mode` varchar(32) NOT NULL,
    `Status` varchar(32) NOT NULL,
    `SourceLabel` varchar(256) NOT NULL,
    `StartedAtUtc` datetime(6) NOT NULL,
    `CompletedAtUtc` datetime(6) NULL,
    `NewCount` int NOT NULL,
    `UnchangedCount` int NOT NULL,
    `ChangedCount` int NOT NULL,
    `MissingCount` int NOT NULL,
    `InvalidCount` int NOT NULL,
    `OrphanedCount` int NOT NULL,
    `AppliedCount` int NOT NULL,
    `FailedCount` int NOT NULL,
    `ErrorSummary` longtext NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `LegacySourceSnapshots` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `SourceTable` varchar(64) NOT NULL,
    `SourceId` bigint NOT NULL,
    `Fingerprint` varchar(64) NOT NULL,
    `PayloadJson` longtext NOT NULL,
    `AcceptedAtUtc` datetime(6) NOT NULL,
    `AcceptedFromRunId` bigint NULL,
    `LastSeenAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `LegacyImportRowStates` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `LegacyImportRunId` bigint NOT NULL,
    `SourceTable` varchar(64) NOT NULL,
    `SourceId` bigint NOT NULL,
    `Classification` varchar(32) NOT NULL,
    `ApplyStatus` varchar(32) NOT NULL,
    `TargetEntityType` varchar(128) NULL,
    `TargetEntityId` bigint NULL,
    `IncomingFingerprint` varchar(64) NOT NULL,
    `BaselineFingerprint` varchar(64) NULL,
    `IncomingPayloadJson` longtext NOT NULL,
    `BaselinePayloadJson` longtext NULL,
    `SourceUpdatedAt` datetime(6) NULL,
    `Error` longtext NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_LegacyImportRowStates_LegacyImportRuns_LegacyImportRunId` FOREIGN KEY (`LegacyImportRunId`) REFERENCES `LegacyImportRuns` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `LegacyImportDifferences` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `LegacyImportRowStateId` bigint NOT NULL,
    `FieldName` varchar(191) NOT NULL,
    `BaselineValue` longtext NULL,
    `IncomingValue` longtext NULL,
    `Decision` varchar(32) NOT NULL,
    `ResolvedValue` longtext NULL,
    `ReviewedBy` varchar(191) NULL,
    `ReviewedAtUtc` datetime(6) NULL,
    `ReviewReason` longtext NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_LegacyImportDifferences_LegacyImportRowStates_LegacyImportRo~` FOREIGN KEY (`LegacyImportRowStateId`) REFERENCES `LegacyImportRowStates` (`Id`) ON DELETE CASCADE
);

CREATE INDEX `IX_LegacyImportDifferences_Decision` ON `LegacyImportDifferences` (`Decision`);

CREATE UNIQUE INDEX `IX_LegacyImportDifferences_LegacyImportRowStateId_FieldName` ON `LegacyImportDifferences` (`LegacyImportRowStateId`, `FieldName`);

CREATE INDEX `IX_LegacyImportRowStates_LegacyImportRunId_Classification` ON `LegacyImportRowStates` (`LegacyImportRunId`, `Classification`);

CREATE UNIQUE INDEX `IX_LegacyImportRowStates_LegacyImportRunId_SourceTable_SourceId` ON `LegacyImportRowStates` (`LegacyImportRunId`, `SourceTable`, `SourceId`);

CREATE INDEX `IX_LegacyImportRuns_StartedAtUtc` ON `LegacyImportRuns` (`StartedAtUtc`);

CREATE INDEX `IX_LegacyImportRuns_Status` ON `LegacyImportRuns` (`Status`);

CREATE INDEX `IX_LegacySourceSnapshots_LastSeenAtUtc` ON `LegacySourceSnapshots` (`LastSeenAtUtc`);

CREATE UNIQUE INDEX `IX_LegacySourceSnapshots_SourceTable_SourceId` ON `LegacySourceSnapshots` (`SourceTable`, `SourceId`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260722094552_AddIncrementalLegacyReconciliation', '10.0.8');

COMMIT;

