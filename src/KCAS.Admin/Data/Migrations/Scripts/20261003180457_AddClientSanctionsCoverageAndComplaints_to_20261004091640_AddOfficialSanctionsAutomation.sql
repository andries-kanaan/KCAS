START TRANSACTION;
CREATE TABLE `SanctionsSourceSnapshots` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `SourceUrl` varchar(1024) NOT NULL,
    `ContentSha256` varchar(64) NOT NULL,
    `Payload` longblob NOT NULL,
    `RetrievedAtUtc` datetime(6) NOT NULL,
    `PublishedAtUtc` datetime(6) NULL,
    `Individuals` int NOT NULL,
    `Entities` int NOT NULL,
    `ClientSanctionsBatchId` int NOT NULL,
    `EmployeeTfsBatchId` int NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_SanctionsSourceSnapshots_ClientSanctionsBatches_ClientSancti~` FOREIGN KEY (`ClientSanctionsBatchId`) REFERENCES `ClientSanctionsBatches` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_SanctionsSourceSnapshots_EmployeeTfsBatches_EmployeeTfsBatch~` FOREIGN KEY (`EmployeeTfsBatchId`) REFERENCES `EmployeeTfsBatches` (`Id`) ON DELETE RESTRICT
);

CREATE TABLE `SanctionsAutomatedResults` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `SanctionsSourceSnapshotId` int NOT NULL,
    `ClientSanctionsSubjectId` int NULL,
    `EmployeeProfileId` int NULL,
    `ScopeHash` varchar(64) NOT NULL,
    `ScopeJson` longtext NOT NULL,
    `Outcome` varchar(32) NOT NULL,
    `CandidatesJson` longtext NOT NULL,
    `Finding` longtext NOT NULL,
    `PerformedAtUtc` datetime(6) NOT NULL,
    `ClientEvidenceItemId` int NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_SanctionsAutomatedResults_ClientEvidenceItems_ClientEvidence~` FOREIGN KEY (`ClientEvidenceItemId`) REFERENCES `ClientEvidenceItems` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_SanctionsAutomatedResults_ClientSanctionsSubjects_ClientSanc~` FOREIGN KEY (`ClientSanctionsSubjectId`) REFERENCES `ClientSanctionsSubjects` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_SanctionsAutomatedResults_EmployeeProfiles_EmployeeProfileId` FOREIGN KEY (`EmployeeProfileId`) REFERENCES `EmployeeProfiles` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_SanctionsAutomatedResults_SanctionsSourceSnapshots_Sanctions~` FOREIGN KEY (`SanctionsSourceSnapshotId`) REFERENCES `SanctionsSourceSnapshots` (`Id`) ON DELETE RESTRICT
);

CREATE TABLE `SanctionsSourceChecks` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `StartedAtUtc` datetime(6) NOT NULL,
    `CompletedAtUtc` datetime(6) NOT NULL,
    `SourceUrl` varchar(1024) NOT NULL,
    `Outcome` varchar(32) NOT NULL,
    `SanctionsSourceSnapshotId` int NULL,
    `Detail` longtext NOT NULL,
    `SubjectsChecked` int NOT NULL,
    `NoCandidates` int NOT NULL,
    `NeedsReview` int NOT NULL,
    `MaximumSourceAgeHours` int NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_SanctionsSourceChecks_SanctionsSourceSnapshots_SanctionsSour~` FOREIGN KEY (`SanctionsSourceSnapshotId`) REFERENCES `SanctionsSourceSnapshots` (`Id`) ON DELETE RESTRICT
);

CREATE INDEX `IX_SanctionsAutomatedResults_ClientEvidenceItemId` ON `SanctionsAutomatedResults` (`ClientEvidenceItemId`);

CREATE INDEX `IX_SanctionsAutomatedResults_ClientSanctionsSubjectId_Performed~` ON `SanctionsAutomatedResults` (`ClientSanctionsSubjectId`, `PerformedAtUtc`);

CREATE INDEX `IX_SanctionsAutomatedResults_EmployeeProfileId` ON `SanctionsAutomatedResults` (`EmployeeProfileId`);

CREATE INDEX `IX_SanctionsAutomatedResults_SanctionsSourceSnapshotId_Employee~` ON `SanctionsAutomatedResults` (`SanctionsSourceSnapshotId`, `EmployeeProfileId`, `PerformedAtUtc`);

CREATE INDEX `IX_SanctionsSourceChecks_CompletedAtUtc` ON `SanctionsSourceChecks` (`CompletedAtUtc`);

CREATE INDEX `IX_SanctionsSourceChecks_SanctionsSourceSnapshotId` ON `SanctionsSourceChecks` (`SanctionsSourceSnapshotId`);

CREATE INDEX `IX_SanctionsSourceSnapshots_ClientSanctionsBatchId` ON `SanctionsSourceSnapshots` (`ClientSanctionsBatchId`);

CREATE INDEX `IX_SanctionsSourceSnapshots_ContentSha256` ON `SanctionsSourceSnapshots` (`ContentSha256`);

CREATE INDEX `IX_SanctionsSourceSnapshots_EmployeeTfsBatchId` ON `SanctionsSourceSnapshots` (`EmployeeTfsBatchId`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261004091640_AddOfficialSanctionsAutomation', '10.0.10');

COMMIT;

