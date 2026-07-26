START TRANSACTION;
CREATE TABLE `RmcpVersions` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `BusinessRiskAssessmentId` int NOT NULL,
    `Title` varchar(191) NOT NULL,
    `VersionReference` varchar(64) NOT NULL,
    `Status` varchar(32) NOT NULL,
    `Scope` longtext NOT NULL,
    `Owner` varchar(191) NOT NULL,
    `ReviewMonths` int NOT NULL,
    `EffectiveDate` date NULL,
    `NextReviewDate` date NULL,
    `SignedDocumentLocation` varchar(1024) NOT NULL,
    `ApprovalResolutionLocation` varchar(1024) NOT NULL,
    `ChangeSummary` longtext NOT NULL,
    `SnapshotJson` longtext NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    `SubmittedAtUtc` datetime(6) NULL,
    `ApprovedAtUtc` datetime(6) NULL,
    `ActivatedAtUtc` datetime(6) NULL,
    `PreparedBy` varchar(191) NULL,
    `UpdatedBy` varchar(191) NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_RmcpVersions_BusinessRiskAssessments_BusinessRiskAssessmentId` FOREIGN KEY (`BusinessRiskAssessmentId`) REFERENCES `BusinessRiskAssessments` (`Id`) ON DELETE RESTRICT
);

CREATE TABLE `RmcpControls` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `RmcpVersionId` int NOT NULL,
    `BusinessRiskItemId` int NULL,
    `Domain` varchar(48) NOT NULL,
    `Code` varchar(64) NOT NULL,
    `Title` varchar(191) NOT NULL,
    `ProcedureSummary` longtext NOT NULL,
    `Owner` varchar(191) NOT NULL,
    `Frequency` varchar(64) NOT NULL,
    `EvidenceExpectation` longtext NOT NULL,
    `MonitoringMethod` longtext NOT NULL,
    `EscalationProcedure` longtext NOT NULL,
    `HasGap` tinyint(1) NOT NULL,
    `GapDescription` longtext NULL,
    `TreatmentOwner` varchar(191) NULL,
    `TreatmentDueDate` date NULL,
    `ComplianceTaskId` int NULL,
    `SortOrder` int NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_RmcpControls_BusinessRiskItems_BusinessRiskItemId` FOREIGN KEY (`BusinessRiskItemId`) REFERENCES `BusinessRiskItems` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_RmcpControls_ComplianceTasks_ComplianceTaskId` FOREIGN KEY (`ComplianceTaskId`) REFERENCES `ComplianceTasks` (`Id`) ON DELETE SET NULL,
    CONSTRAINT `FK_RmcpControls_RmcpVersions_RmcpVersionId` FOREIGN KEY (`RmcpVersionId`) REFERENCES `RmcpVersions` (`Id`) ON DELETE CASCADE
);

CREATE INDEX `IX_RmcpControls_BusinessRiskItemId` ON `RmcpControls` (`BusinessRiskItemId`);

CREATE INDEX `IX_RmcpControls_ComplianceTaskId` ON `RmcpControls` (`ComplianceTaskId`);

CREATE UNIQUE INDEX `IX_RmcpControls_RmcpVersionId_Code` ON `RmcpControls` (`RmcpVersionId`, `Code`);

CREATE INDEX `IX_RmcpVersions_BusinessRiskAssessmentId` ON `RmcpVersions` (`BusinessRiskAssessmentId`);

CREATE INDEX `IX_RmcpVersions_Status_VersionReference` ON `RmcpVersions` (`Status`, `VersionReference`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260726164754_AddRmcpControlFoundation', '10.0.10');

COMMIT;

