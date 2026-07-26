START TRANSACTION;
ALTER TABLE `RiskBands` ADD `ReviewMonths` int NOT NULL DEFAULT 36;

CREATE TABLE `ClientRiskAssessments` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientId` int NOT NULL,
    `RiskMethodologyVersionId` int NOT NULL,
    `Status` varchar(32) NOT NULL,
    `CalculatedScore` decimal(9,4) NOT NULL,
    `CalculatedRating` varchar(96) NULL,
    `FinalRating` varchar(96) NULL,
    `IsOverride` tinyint(1) NOT NULL,
    `OverrideReason` longtext NULL,
    `HasPepExposure` tinyint(1) NOT NULL,
    `HasSanctionsConcern` tinyint(1) NOT NULL,
    `HasAdverseInformation` tinyint(1) NOT NULL,
    `RequiresEdd` tinyint(1) NOT NULL,
    `StandardControlsApplied` tinyint(1) NOT NULL,
    `Narrative` longtext NULL,
    `EffectiveDate` date NULL,
    `NextReviewDate` date NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    `FinalisedAtUtc` datetime(6) NULL,
    `ApprovedAtUtc` datetime(6) NULL,
    `PreparedBy` varchar(191) NULL,
    `FinalisedBy` varchar(191) NULL,
    `SnapshotJson` longtext NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientRiskAssessments_Clients_ClientId` FOREIGN KEY (`ClientId`) REFERENCES `Clients` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_ClientRiskAssessments_RiskMethodologyVersions_RiskMethodolog~` FOREIGN KEY (`RiskMethodologyVersionId`) REFERENCES `RiskMethodologyVersions` (`Id`) ON DELETE RESTRICT
);

CREATE TABLE `ClientRiskAssessmentApprovals` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientRiskAssessmentId` int NOT NULL,
    `Approver` varchar(191) NOT NULL,
    `Decision` varchar(32) NOT NULL,
    `Reason` longtext NOT NULL,
    `DecidedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientRiskAssessmentApprovals_ClientRiskAssessments_ClientRi~` FOREIGN KEY (`ClientRiskAssessmentId`) REFERENCES `ClientRiskAssessments` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `ClientRiskAssessmentResponses` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientRiskAssessmentId` int NOT NULL,
    `RiskFactorDefinitionId` int NOT NULL,
    `RiskFactorOptionId` int NULL,
    `ClientEvidenceItemId` int NULL,
    `Score` int NOT NULL,
    `WeightedScore` decimal(9,4) NOT NULL,
    `Explanation` longtext NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientRiskAssessmentResponses_ClientEvidenceItems_ClientEvid~` FOREIGN KEY (`ClientEvidenceItemId`) REFERENCES `ClientEvidenceItems` (`Id`) ON DELETE SET NULL,
    CONSTRAINT `FK_ClientRiskAssessmentResponses_ClientRiskAssessments_ClientRi~` FOREIGN KEY (`ClientRiskAssessmentId`) REFERENCES `ClientRiskAssessments` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_ClientRiskAssessmentResponses_RiskFactorDefinitions_RiskFact~` FOREIGN KEY (`RiskFactorDefinitionId`) REFERENCES `RiskFactorDefinitions` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_ClientRiskAssessmentResponses_RiskFactorOptions_RiskFactorOp~` FOREIGN KEY (`RiskFactorOptionId`) REFERENCES `RiskFactorOptions` (`Id`) ON DELETE RESTRICT
);

CREATE UNIQUE INDEX `IX_ClientRiskAssessmentApprovals_ClientRiskAssessmentId_Approver` ON `ClientRiskAssessmentApprovals` (`ClientRiskAssessmentId`, `Approver`);

CREATE INDEX `IX_ClientRiskAssessmentResponses_ClientEvidenceItemId` ON `ClientRiskAssessmentResponses` (`ClientEvidenceItemId`);

CREATE UNIQUE INDEX `IX_ClientRiskAssessmentResponses_ClientRiskAssessmentId_RiskFac~` ON `ClientRiskAssessmentResponses` (`ClientRiskAssessmentId`, `RiskFactorDefinitionId`);

CREATE INDEX `IX_ClientRiskAssessmentResponses_RiskFactorDefinitionId` ON `ClientRiskAssessmentResponses` (`RiskFactorDefinitionId`);

CREATE INDEX `IX_ClientRiskAssessmentResponses_RiskFactorOptionId` ON `ClientRiskAssessmentResponses` (`RiskFactorOptionId`);

CREATE INDEX `IX_ClientRiskAssessments_ClientId_Status` ON `ClientRiskAssessments` (`ClientId`, `Status`);

CREATE INDEX `IX_ClientRiskAssessments_FinalRating_Status` ON `ClientRiskAssessments` (`FinalRating`, `Status`);

CREATE INDEX `IX_ClientRiskAssessments_NextReviewDate` ON `ClientRiskAssessments` (`NextReviewDate`);

CREATE INDEX `IX_ClientRiskAssessments_RiskMethodologyVersionId` ON `ClientRiskAssessments` (`RiskMethodologyVersionId`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260726131207_AddProportionalClientRisk', '10.0.10');

COMMIT;

