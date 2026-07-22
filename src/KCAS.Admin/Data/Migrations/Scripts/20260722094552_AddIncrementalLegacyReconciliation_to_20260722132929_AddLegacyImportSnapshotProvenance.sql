START TRANSACTION;
ALTER TABLE `LegacyImportRuns` ADD `ApprovedScanRunId` bigint NULL;

ALTER TABLE `LegacyImportRuns` ADD `SourceSnapshotFileName` varchar(260) NULL;

ALTER TABLE `LegacyImportRuns` ADD `SourceSnapshotSha256` varchar(64) NOT NULL DEFAULT '';

CREATE INDEX `IX_LegacyImportRuns_SourceSnapshotSha256` ON `LegacyImportRuns` (`SourceSnapshotSha256`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260722132929_AddLegacyImportSnapshotProvenance', '10.0.10');

COMMIT;

