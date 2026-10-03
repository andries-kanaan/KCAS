START TRANSACTION;
CREATE TABLE `ClientSanctionsBatches` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `SourceVersion` varchar(191) NOT NULL,
    `SourceUrl` varchar(1024) NOT NULL,
    `SourcePublishedAtUtc` datetime(6) NOT NULL,
    `Reason` longtext NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `CreatedBy` varchar(191) NOT NULL,
    `Version` varchar(32) NOT NULL,
    `RecipientUserIdsJson` longtext NOT NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `ComplaintCases` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientId` int NULL,
    `ComplainantName` varchar(240) NOT NULL,
    `ContactDetails` longtext NOT NULL,
    `ReceivedAtUtc` datetime(6) NULL,
    `Channel` varchar(48) NOT NULL,
    `Allegation` longtext NOT NULL,
    `RequestedOutcome` longtext NOT NULL,
    `Category` varchar(96) NOT NULL,
    `SecondaryThemes` longtext NOT NULL,
    `HandlerUserId` varchar(191) NOT NULL,
    `ImplicatedUserId` varchar(191) NULL,
    `IsReportable` tinyint(1) NOT NULL,
    `Status` varchar(32) NOT NULL,
    `Decision` varchar(48) NULL,
    `DecisionReasons` longtext NULL,
    `Remedy` longtext NULL,
    `CompensationAwarded` decimal(18,2) NOT NULL,
    `GoodwillAwarded` decimal(18,2) NOT NULL,
    `DecidedAtUtc` datetime(6) NULL,
    `DecidedBy` varchar(191) NULL,
    `NextUpdateDate` date NULL,
    `ComplianceTaskId` int NULL,
    `LegacyKey` varchar(64) NULL,
    `LegacyRowHash` varchar(64) NULL,
    `LegacySourceJson` longtext NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    `UpdatedBy` varchar(191) NOT NULL,
    `Version` varchar(32) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ComplaintCases_Clients_ClientId` FOREIGN KEY (`ClientId`) REFERENCES `Clients` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_ComplaintCases_ComplianceTasks_ComplianceTaskId` FOREIGN KEY (`ComplianceTaskId`) REFERENCES `ComplianceTasks` (`Id`) ON DELETE RESTRICT
);

CREATE TABLE `ClientSanctionsSubjects` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientSanctionsBatchId` int NOT NULL,
    `ClientId` int NOT NULL,
    `SubjectKey` varchar(96) NOT NULL,
    `ScopeHash` varchar(64) NOT NULL,
    `SubjectType` varchar(96) NOT NULL,
    `SubjectName` varchar(240) NOT NULL,
    `ClientRelatedPartyId` int NULL,
    `IdentitySummary` longtext NOT NULL,
    `IsCurrent` tinyint(1) NOT NULL,
    `ComplianceTaskId` int NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientSanctionsSubjects_ClientSanctionsBatches_ClientSanctio~` FOREIGN KEY (`ClientSanctionsBatchId`) REFERENCES `ClientSanctionsBatches` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_ClientSanctionsSubjects_Clients_ClientId` FOREIGN KEY (`ClientId`) REFERENCES `Clients` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_ClientSanctionsSubjects_ComplianceTasks_ComplianceTaskId` FOREIGN KEY (`ComplianceTaskId`) REFERENCES `ComplianceTasks` (`Id`) ON DELETE RESTRICT
);

CREATE TABLE `ComplaintEvents` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ComplaintCaseId` int NOT NULL,
    `Kind` varchar(48) NOT NULL,
    `OccurredAtUtc` datetime(6) NOT NULL,
    `Details` longtext NOT NULL,
    `EvidenceReference` longtext NOT NULL,
    `PerformedBy` varchar(191) NOT NULL,
    `RecourseDetails` longtext NULL,
    `Amount` decimal(18,2) NULL,
    `RecordedBy` varchar(191) NOT NULL,
    `RecordedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ComplaintEvents_ComplaintCases_ComplaintCaseId` FOREIGN KEY (`ComplaintCaseId`) REFERENCES `ComplaintCases` (`Id`) ON DELETE RESTRICT
);

CREATE TABLE `ClientSanctionsCoverageRecords` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientSanctionsSubjectId` int NOT NULL,
    `ClientEvidenceItemId` int NULL,
    `EvidenceFingerprint` varchar(64) NULL,
    `Outcome` varchar(64) NOT NULL,
    `Reason` longtext NOT NULL,
    `ExclusionReference` longtext NULL,
    `RecordedBy` varchar(191) NOT NULL,
    `RecordedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientSanctionsCoverageRecords_ClientEvidenceItems_ClientEvi~` FOREIGN KEY (`ClientEvidenceItemId`) REFERENCES `ClientEvidenceItems` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_ClientSanctionsCoverageRecords_ClientSanctionsSubjects_Clien~` FOREIGN KEY (`ClientSanctionsSubjectId`) REFERENCES `ClientSanctionsSubjects` (`Id`) ON DELETE RESTRICT
);

CREATE UNIQUE INDEX `IX_ClientSanctionsBatches_SourceVersion` ON `ClientSanctionsBatches` (`SourceVersion`);

CREATE INDEX `IX_ClientSanctionsCoverageRecords_ClientEvidenceItemId` ON `ClientSanctionsCoverageRecords` (`ClientEvidenceItemId`);

CREATE INDEX `IX_ClientSanctionsCoverageRecords_ClientSanctionsSubjectId_Reco~` ON `ClientSanctionsCoverageRecords` (`ClientSanctionsSubjectId`, `RecordedAtUtc`);

CREATE INDEX `IX_ClientSanctionsSubjects_ClientId` ON `ClientSanctionsSubjects` (`ClientId`);

CREATE UNIQUE INDEX `IX_ClientSanctionsSubjects_ClientSanctionsBatchId_SubjectKey_Sc~` ON `ClientSanctionsSubjects` (`ClientSanctionsBatchId`, `SubjectKey`, `ScopeHash`);

CREATE INDEX `IX_ClientSanctionsSubjects_ComplianceTaskId` ON `ClientSanctionsSubjects` (`ComplianceTaskId`);

CREATE INDEX `IX_ComplaintCases_ClientId` ON `ComplaintCases` (`ClientId`);

CREATE INDEX `IX_ComplaintCases_ComplianceTaskId` ON `ComplaintCases` (`ComplianceTaskId`);

CREATE UNIQUE INDEX `IX_ComplaintCases_LegacyKey` ON `ComplaintCases` (`LegacyKey`);

CREATE INDEX `IX_ComplaintCases_Status_ReceivedAtUtc` ON `ComplaintCases` (`Status`, `ReceivedAtUtc`);

CREATE INDEX `IX_ComplaintEvents_ComplaintCaseId_OccurredAtUtc` ON `ComplaintEvents` (`ComplaintCaseId`, `OccurredAtUtc`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261003180457_AddClientSanctionsCoverageAndComplaints', '10.0.10');

COMMIT;

