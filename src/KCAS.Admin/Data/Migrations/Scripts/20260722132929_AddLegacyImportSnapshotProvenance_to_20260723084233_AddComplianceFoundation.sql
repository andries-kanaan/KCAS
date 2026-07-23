START TRANSACTION;
CREATE TABLE `ComplianceApprovals` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `TargetEntityType` varchar(128) NOT NULL,
    `TargetEntityId` int NOT NULL,
    `Decision` varchar(32) NOT NULL,
    `Approver` varchar(191) NULL,
    `DecidedAtUtc` datetime(6) NOT NULL,
    `Reason` longtext NOT NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `ComplianceAuditEvents` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `EntityType` varchar(128) NOT NULL,
    `EntityId` int NOT NULL,
    `Action` varchar(64) NOT NULL,
    `OldValueJson` longtext NULL,
    `NewValueJson` longtext NULL,
    `UserName` varchar(191) NULL,
    `TimestampUtc` datetime(6) NOT NULL,
    `Reason` longtext NOT NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `ComplianceEvidence` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `EvidenceType` varchar(96) NOT NULL,
    `Title` varchar(240) NOT NULL,
    `Source` varchar(191) NULL,
    `Location` longtext NULL,
    `ReceivedDate` date NULL,
    `VerifiedDate` date NULL,
    `ExpiryDate` date NULL,
    `Reviewer` varchar(191) NULL,
    `Notes` longtext NULL,
    `LinkedEntityType` varchar(128) NULL,
    `LinkedEntityId` int NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NULL,
    `UpdatedBy` varchar(191) NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `ComplianceProfiles` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `LegalName` varchar(200) NOT NULL,
    `TradingName` varchar(200) NULL,
    `FspNumber` varchar(64) NULL,
    `AccountableInstitutionNumber` varchar(64) NULL,
    `PrimaryContactName` varchar(191) NULL,
    `PrimaryContactEmail` varchar(191) NULL,
    `PrimaryContactPhone` varchar(64) NULL,
    `RegisteredAddress` longtext NULL,
    `OperatingAddress` longtext NULL,
    `EffectiveFrom` date NULL,
    `EffectiveTo` date NULL,
    `Status` varchar(32) NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NULL,
    `UpdatedBy` varchar(191) NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `ComplianceReferenceValues` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Category` varchar(96) NOT NULL,
    `Code` varchar(96) NOT NULL,
    `Name` varchar(191) NOT NULL,
    `Description` longtext NULL,
    `SortOrder` int NOT NULL,
    `IsActive` tinyint(1) NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NULL,
    `UpdatedBy` varchar(191) NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `ComplianceTasks` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Title` varchar(240) NOT NULL,
    `Description` longtext NULL,
    `Owner` varchar(191) NULL,
    `DueDate` date NULL,
    `Priority` varchar(32) NOT NULL,
    `Status` varchar(32) NOT NULL,
    `LinkedEntityType` varchar(128) NULL,
    `LinkedEntityId` int NULL,
    `ClosureNotes` longtext NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `ClosedAtUtc` datetime(6) NULL,
    `UpdatedAtUtc` datetime(6) NULL,
    `UpdatedBy` varchar(191) NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `ControlledDocuments` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `DocumentType` varchar(96) NOT NULL,
    `Title` varchar(240) NOT NULL,
    `Owner` varchar(191) NULL,
    `VersionReference` varchar(96) NULL,
    `Status` varchar(32) NOT NULL,
    `EffectiveDate` date NULL,
    `NextReviewDate` date NULL,
    `Location` longtext NULL,
    `Notes` longtext NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NULL,
    `UpdatedBy` varchar(191) NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `GovernanceRoleAssignments` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `RoleType` varchar(96) NOT NULL,
    `PersonName` varchar(191) NOT NULL,
    `Email` varchar(191) NULL,
    `Phone` varchar(64) NULL,
    `ResponsibilitySummary` longtext NULL,
    `StartDate` date NULL,
    `EndDate` date NULL,
    `IsActive` tinyint(1) NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NULL,
    `UpdatedBy` varchar(191) NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `RiskMethodologyVersions` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Name` varchar(191) NOT NULL,
    `VersionLabel` varchar(64) NULL,
    `Status` varchar(32) NOT NULL,
    `EffectiveFrom` date NULL,
    `EffectiveTo` date NULL,
    `Summary` longtext NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `SubmittedAtUtc` datetime(6) NULL,
    `ApprovedAtUtc` datetime(6) NULL,
    `ActivatedAtUtc` datetime(6) NULL,
    `UpdatedBy` varchar(191) NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `RiskBands` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `RiskMethodologyVersionId` int NOT NULL,
    `Name` varchar(96) NOT NULL,
    `MinimumScore` decimal(9,4) NOT NULL,
    `MaximumScore` decimal(9,4) NULL,
    `SortOrder` int NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_RiskBands_RiskMethodologyVersions_RiskMethodologyVersionId` FOREIGN KEY (`RiskMethodologyVersionId`) REFERENCES `RiskMethodologyVersions` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `RiskFactorDefinitions` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `RiskMethodologyVersionId` int NOT NULL,
    `Code` varchar(96) NOT NULL,
    `Name` varchar(191) NOT NULL,
    `Description` longtext NULL,
    `Weight` decimal(9,4) NOT NULL,
    `IsMandatoryHighRiskTrigger` tinyint(1) NOT NULL,
    `SortOrder` int NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_RiskFactorDefinitions_RiskMethodologyVersions_RiskMethodolog~` FOREIGN KEY (`RiskMethodologyVersionId`) REFERENCES `RiskMethodologyVersions` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `RiskFactorOptions` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `RiskFactorDefinitionId` int NOT NULL,
    `Code` varchar(96) NOT NULL,
    `Label` varchar(191) NOT NULL,
    `Score` int NOT NULL,
    `TriggersHighRisk` tinyint(1) NOT NULL,
    `SortOrder` int NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_RiskFactorOptions_RiskFactorDefinitions_RiskFactorDefinition~` FOREIGN KEY (`RiskFactorDefinitionId`) REFERENCES `RiskFactorDefinitions` (`Id`) ON DELETE CASCADE
);

CREATE INDEX `IX_ComplianceApprovals_TargetEntityType_TargetEntityId` ON `ComplianceApprovals` (`TargetEntityType`, `TargetEntityId`);

CREATE INDEX `IX_ComplianceAuditEvents_EntityType_EntityId` ON `ComplianceAuditEvents` (`EntityType`, `EntityId`);

CREATE INDEX `IX_ComplianceAuditEvents_TimestampUtc` ON `ComplianceAuditEvents` (`TimestampUtc`);

CREATE INDEX `IX_ComplianceEvidence_EvidenceType_ExpiryDate` ON `ComplianceEvidence` (`EvidenceType`, `ExpiryDate`);

CREATE INDEX `IX_ComplianceEvidence_LinkedEntityType_LinkedEntityId` ON `ComplianceEvidence` (`LinkedEntityType`, `LinkedEntityId`);

CREATE INDEX `IX_ComplianceProfiles_Status` ON `ComplianceProfiles` (`Status`);

CREATE UNIQUE INDEX `IX_ComplianceReferenceValues_Category_Code_IsActive` ON `ComplianceReferenceValues` (`Category`, `Code`, `IsActive`);

CREATE INDEX `IX_ComplianceReferenceValues_Category_SortOrder` ON `ComplianceReferenceValues` (`Category`, `SortOrder`);

CREATE INDEX `IX_ComplianceTasks_LinkedEntityType_LinkedEntityId` ON `ComplianceTasks` (`LinkedEntityType`, `LinkedEntityId`);

CREATE INDEX `IX_ComplianceTasks_Status_DueDate` ON `ComplianceTasks` (`Status`, `DueDate`);

CREATE INDEX `IX_ControlledDocuments_DocumentType_Status` ON `ControlledDocuments` (`DocumentType`, `Status`);

CREATE INDEX `IX_ControlledDocuments_NextReviewDate` ON `ControlledDocuments` (`NextReviewDate`);

CREATE INDEX `IX_GovernanceRoleAssignments_RoleType_IsActive` ON `GovernanceRoleAssignments` (`RoleType`, `IsActive`);

CREATE UNIQUE INDEX `IX_RiskBands_RiskMethodologyVersionId_Name` ON `RiskBands` (`RiskMethodologyVersionId`, `Name`);

CREATE UNIQUE INDEX `IX_RiskFactorDefinitions_RiskMethodologyVersionId_Code` ON `RiskFactorDefinitions` (`RiskMethodologyVersionId`, `Code`);

CREATE UNIQUE INDEX `IX_RiskFactorOptions_RiskFactorDefinitionId_Code` ON `RiskFactorOptions` (`RiskFactorDefinitionId`, `Code`);

CREATE INDEX `IX_RiskMethodologyVersions_EffectiveFrom` ON `RiskMethodologyVersions` (`EffectiveFrom`);

CREATE INDEX `IX_RiskMethodologyVersions_Status` ON `RiskMethodologyVersions` (`Status`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260723084233_AddComplianceFoundation', '10.0.10');

COMMIT;

