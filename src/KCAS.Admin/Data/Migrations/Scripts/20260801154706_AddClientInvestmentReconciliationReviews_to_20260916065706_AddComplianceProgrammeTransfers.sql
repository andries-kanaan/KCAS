START TRANSACTION;
CREATE TABLE `ComplianceProgrammeTransferRecords` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `PackageId` varchar(36) NOT NULL,
    `Direction` varchar(16) NOT NULL,
    `ContentSha256` varchar(64) NOT NULL,
    `Status` varchar(32) NOT NULL,
    `FileName` varchar(260) NOT NULL,
    `StoragePath` varchar(512) NOT NULL,
    `BusinessRiskAssessmentId` int NULL,
    `RmcpVersionId` int NULL,
    `ControlledDocumentCount` int NOT NULL,
    `EvidenceCount` int NOT NULL,
    `SummaryJson` longtext NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `AppliedAtUtc` datetime(6) NULL,
    `AppliedBy` varchar(191) NULL,
    PRIMARY KEY (`Id`)
);

CREATE INDEX `IX_ComplianceProgrammeTransferRecords_BusinessRiskAssessmentId_~` ON `ComplianceProgrammeTransferRecords` (`BusinessRiskAssessmentId`, `RmcpVersionId`);

CREATE INDEX `IX_ComplianceProgrammeTransferRecords_Direction_ContentSha256` ON `ComplianceProgrammeTransferRecords` (`Direction`, `ContentSha256`);

CREATE INDEX `IX_ComplianceProgrammeTransferRecords_Direction_CreatedAtUtc` ON `ComplianceProgrammeTransferRecords` (`Direction`, `CreatedAtUtc`);

CREATE UNIQUE INDEX `IX_ComplianceProgrammeTransferRecords_Direction_PackageId` ON `ComplianceProgrammeTransferRecords` (`Direction`, `PackageId`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260916065706_AddComplianceProgrammeTransfers', '10.0.10');

COMMIT;
