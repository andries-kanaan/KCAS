START TRANSACTION;
CREATE TABLE `BusinessRiskAssessments` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Name` varchar(191) NOT NULL,
    `AssessmentYear` int NOT NULL,
    `AsAtDate` date NOT NULL,
    `Status` varchar(32) NOT NULL,
    `Scope` longtext NOT NULL,
    `MethodologyNarrative` longtext NOT NULL,
    `ManagementJudgement` longtext NOT NULL,
    `Limitations` longtext NOT NULL,
    `RiskTolerance` longtext NOT NULL,
    `PortfolioSnapshotJson` longtext NULL,
    `SnapshotJson` longtext NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    `SubmittedAtUtc` datetime(6) NULL,
    `ApprovedAtUtc` datetime(6) NULL,
    `ActivatedAtUtc` datetime(6) NULL,
    `PreparedBy` varchar(191) NULL,
    `UpdatedBy` varchar(191) NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `BusinessRiskApprovals` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `BusinessRiskAssessmentId` int NOT NULL,
    `Approver` varchar(191) NOT NULL,
    `Reason` longtext NOT NULL,
    `ApprovedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_BusinessRiskApprovals_BusinessRiskAssessments_BusinessRiskAs~` FOREIGN KEY (`BusinessRiskAssessmentId`) REFERENCES `BusinessRiskAssessments` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `BusinessRiskItems` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `BusinessRiskAssessmentId` int NOT NULL,
    `Category` varchar(48) NOT NULL,
    `RiskStatement` longtext NOT NULL,
    `EvidenceAndRationale` longtext NOT NULL,
    `Likelihood` int NOT NULL,
    `Impact` int NOT NULL,
    `InherentScore` int NOT NULL,
    `InherentRating` varchar(32) NOT NULL,
    `KeyControls` longtext NOT NULL,
    `ControlEffectiveness` varchar(32) NOT NULL,
    `ResidualRating` varchar(32) NOT NULL,
    `ResidualRationale` longtext NOT NULL,
    `TreatmentDecision` varchar(32) NOT NULL,
    `Owner` varchar(191) NOT NULL,
    `DueDate` date NULL,
    `SortOrder` int NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_BusinessRiskItems_BusinessRiskAssessments_BusinessRiskAssess~` FOREIGN KEY (`BusinessRiskAssessmentId`) REFERENCES `BusinessRiskAssessments` (`Id`) ON DELETE CASCADE
);

CREATE UNIQUE INDEX `IX_BusinessRiskApprovals_BusinessRiskAssessmentId_Approver` ON `BusinessRiskApprovals` (`BusinessRiskAssessmentId`, `Approver`);

CREATE INDEX `IX_BusinessRiskAssessments_AssessmentYear_Status` ON `BusinessRiskAssessments` (`AssessmentYear`, `Status`);

CREATE INDEX `IX_BusinessRiskItems_BusinessRiskAssessmentId_Category` ON `BusinessRiskItems` (`BusinessRiskAssessmentId`, `Category`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260726141855_AddBusinessRiskAssessmentFoundation', '10.0.10');

COMMIT;

