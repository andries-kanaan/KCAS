START TRANSACTION;
CREATE TABLE `ClientAdviceCases` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientId` int NOT NULL,
    `PreviousAdviceCaseId` int NULL,
    `AdviceType` varchar(48) NOT NULL,
    `Status` varchar(32) NOT NULL,
    `Revision` int NOT NULL,
    `AdviceDate` date NOT NULL,
    `AdviserName` varchar(191) NOT NULL,
    `PreparedBy` varchar(191) NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    `AdviceScope` longtext NOT NULL,
    `MeetingSummary` longtext NOT NULL,
    `NeedsAndObjectives` longtext NOT NULL,
    `FinancialSituation` longtext NOT NULL,
    `AdviceLimitations` longtext NOT NULL,
    `ProductKnowledgeSummary` longtext NOT NULL,
    `InvestmentAmount` decimal(18,2) NULL,
    `InvestmentPortfolioPercent` decimal(18,2) NULL,
    `DomesticPreferencePercent` decimal(18,2) NULL,
    `OffshorePreferencePercent` decimal(18,2) NULL,
    `CalculatedRiskScore` int NULL,
    `CalculatedRiskLevel` varchar(48) NULL,
    `FinalRiskLevel` varchar(48) NULL,
    `RiskOverrideReason` longtext NULL,
    `RecommendationSummary` longtext NOT NULL,
    `RecommendationRationale` longtext NOT NULL,
    `CostsAndFees` longtext NOT NULL,
    `TaxConsequences` longtext NOT NULL,
    `LiquidityAndRestrictions` longtext NOT NULL,
    `MaterialRisks` longtext NOT NULL,
    `IsReplacement` tinyint(1) NOT NULL,
    `ReplacementConsequences` longtext NOT NULL,
    `ClientDeparture` longtext NOT NULL,
    `WarningsGiven` longtext NOT NULL,
    `FrozenSnapshotJson` longtext NULL,
    `FrozenSnapshotSha256` varchar(64) NULL,
    `SubmittedAtUtc` datetime(6) NULL,
    `ApprovedAtUtc` datetime(6) NULL,
    `IssuedAtUtc` datetime(6) NULL,
    `CompletedAtUtc` datetime(6) NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientAdviceCases_ClientAdviceCases_PreviousAdviceCaseId` FOREIGN KEY (`PreviousAdviceCaseId`) REFERENCES `ClientAdviceCases` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_ClientAdviceCases_Clients_ClientId` FOREIGN KEY (`ClientId`) REFERENCES `Clients` (`Id`) ON DELETE RESTRICT
);

CREATE TABLE `ClientAdviceApprovals` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientAdviceCaseId` int NOT NULL,
    `Reviewer` varchar(191) NOT NULL,
    `Decision` varchar(32) NOT NULL,
    `Reason` longtext NOT NULL,
    `DecidedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientAdviceApprovals_ClientAdviceCases_ClientAdviceCaseId` FOREIGN KEY (`ClientAdviceCaseId`) REFERENCES `ClientAdviceCases` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `ClientAdviceDocuments` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientAdviceCaseId` int NULL,
    `ClientId` int NOT NULL,
    `DocumentType` varchar(48) NOT NULL,
    `FileName` varchar(260) NOT NULL,
    `SourcePath` varchar(512) NULL,
    `FileSha256` varchar(64) NULL,
    `FileSizeBytes` bigint NULL,
    `FileLastWriteTimeUtc` datetime(6) NULL,
    `RecordedBy` varchar(191) NULL,
    `RecordedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientAdviceDocuments_ClientAdviceCases_ClientAdviceCaseId` FOREIGN KEY (`ClientAdviceCaseId`) REFERENCES `ClientAdviceCases` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_ClientAdviceDocuments_Clients_ClientId` FOREIGN KEY (`ClientId`) REFERENCES `Clients` (`Id`) ON DELETE RESTRICT
);

CREATE TABLE `ClientAdviceFactSources` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientAdviceCaseId` int NOT NULL,
    `FactName` varchar(96) NOT NULL,
    `SourceDate` date NULL,
    `DocumentPath` varchar(512) NOT NULL,
    `Notes` longtext NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientAdviceFactSources_ClientAdviceCases_ClientAdviceCaseId` FOREIGN KEY (`ClientAdviceCaseId`) REFERENCES `ClientAdviceCases` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `ClientAdviceInvestmentLinks` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientAdviceCaseId` int NOT NULL,
    `ClientInvestmentAccountId` int NOT NULL,
    `Role` varchar(48) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientAdviceInvestmentLinks_ClientAdviceCases_ClientAdviceCa~` FOREIGN KEY (`ClientAdviceCaseId`) REFERENCES `ClientAdviceCases` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_ClientAdviceInvestmentLinks_ClientInvestmentAccounts_ClientI~` FOREIGN KEY (`ClientInvestmentAccountId`) REFERENCES `ClientInvestmentAccounts` (`Id`) ON DELETE RESTRICT
);

CREATE TABLE `ClientAdviceParticipants` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientAdviceCaseId` int NOT NULL,
    `ClientId` int NOT NULL,
    `Role` varchar(48) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientAdviceParticipants_ClientAdviceCases_ClientAdviceCaseId` FOREIGN KEY (`ClientAdviceCaseId`) REFERENCES `ClientAdviceCases` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_ClientAdviceParticipants_Clients_ClientId` FOREIGN KEY (`ClientId`) REFERENCES `Clients` (`Id`) ON DELETE RESTRICT
);

CREATE TABLE `ClientAdviceProducts` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientAdviceCaseId` int NOT NULL,
    `ProductName` varchar(191) NOT NULL,
    `Provider` varchar(191) NULL,
    `ProductType` varchar(96) NULL,
    `IsRecommended` tinyint(1) NOT NULL,
    `Amount` decimal(18,2) NULL,
    `AllocationPercent` decimal(18,2) NULL,
    `Motivation` longtext NULL,
    `SupportingDocumentPath` varchar(512) NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientAdviceProducts_ClientAdviceCases_ClientAdviceCaseId` FOREIGN KEY (`ClientAdviceCaseId`) REFERENCES `ClientAdviceCases` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `ClientAdviceReviewFindings` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientAdviceCaseId` int NOT NULL,
    `Severity` varchar(32) NOT NULL,
    `Category` varchar(96) NOT NULL,
    `AffectedField` varchar(96) NULL,
    `Finding` longtext NOT NULL,
    `EvidenceReference` longtext NULL,
    `RecommendedCorrection` longtext NULL,
    `Status` varchar(32) NOT NULL,
    `Resolution` longtext NULL,
    `PerformedBy` varchar(191) NOT NULL,
    `PerformedAtUtc` datetime(6) NOT NULL,
    `ResolvedBy` varchar(191) NULL,
    `ResolvedAtUtc` datetime(6) NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientAdviceReviewFindings_ClientAdviceCases_ClientAdviceCas~` FOREIGN KEY (`ClientAdviceCaseId`) REFERENCES `ClientAdviceCases` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `ClientAdviceRiskResponses` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientAdviceCaseId` int NOT NULL,
    `QuestionCode` varchar(48) NOT NULL,
    `AnswerCode` varchar(96) NOT NULL,
    `Score` int NOT NULL,
    `Explanation` longtext NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientAdviceRiskResponses_ClientAdviceCases_ClientAdviceCase~` FOREIGN KEY (`ClientAdviceCaseId`) REFERENCES `ClientAdviceCases` (`Id`) ON DELETE CASCADE
);

CREATE INDEX `IX_ClientAdviceApprovals_ClientAdviceCaseId_Reviewer` ON `ClientAdviceApprovals` (`ClientAdviceCaseId`, `Reviewer`);

CREATE INDEX `IX_ClientAdviceCases_ClientId_Status_AdviceDate` ON `ClientAdviceCases` (`ClientId`, `Status`, `AdviceDate`);

CREATE INDEX `IX_ClientAdviceCases_PreviousAdviceCaseId` ON `ClientAdviceCases` (`PreviousAdviceCaseId`);

CREATE INDEX `IX_ClientAdviceDocuments_ClientAdviceCaseId_DocumentType` ON `ClientAdviceDocuments` (`ClientAdviceCaseId`, `DocumentType`);

CREATE INDEX `IX_ClientAdviceDocuments_ClientId_DocumentType_FileSha256` ON `ClientAdviceDocuments` (`ClientId`, `DocumentType`, `FileSha256`);

CREATE INDEX `IX_ClientAdviceFactSources_ClientAdviceCaseId_FactName` ON `ClientAdviceFactSources` (`ClientAdviceCaseId`, `FactName`);

CREATE UNIQUE INDEX `IX_ClientAdviceInvestmentLinks_ClientAdviceCaseId_ClientInvestm~` ON `ClientAdviceInvestmentLinks` (`ClientAdviceCaseId`, `ClientInvestmentAccountId`);

CREATE INDEX `IX_ClientAdviceInvestmentLinks_ClientInvestmentAccountId` ON `ClientAdviceInvestmentLinks` (`ClientInvestmentAccountId`);

CREATE UNIQUE INDEX `IX_ClientAdviceParticipants_ClientAdviceCaseId_ClientId` ON `ClientAdviceParticipants` (`ClientAdviceCaseId`, `ClientId`);

CREATE INDEX `IX_ClientAdviceParticipants_ClientId` ON `ClientAdviceParticipants` (`ClientId`);

CREATE INDEX `IX_ClientAdviceProducts_ClientAdviceCaseId_ProductName` ON `ClientAdviceProducts` (`ClientAdviceCaseId`, `ProductName`);

CREATE INDEX `IX_ClientAdviceReviewFindings_ClientAdviceCaseId_Status` ON `ClientAdviceReviewFindings` (`ClientAdviceCaseId`, `Status`);

CREATE UNIQUE INDEX `IX_ClientAdviceRiskResponses_ClientAdviceCaseId_QuestionCode` ON `ClientAdviceRiskResponses` (`ClientAdviceCaseId`, `QuestionCode`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260918181508_AddClientAdviceWorkflow', '10.0.10');

COMMIT;
