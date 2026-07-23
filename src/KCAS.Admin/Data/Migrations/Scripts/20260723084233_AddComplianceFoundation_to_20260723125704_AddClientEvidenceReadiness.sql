START TRANSACTION;
CREATE TABLE `ClientEvidenceRequirements` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientCategory` varchar(96) NOT NULL,
    `RequirementGroup` varchar(96) NOT NULL,
    `EvidenceType` varchar(96) NOT NULL,
    `Title` varchar(240) NOT NULL,
    `Description` longtext NULL,
    `IsBlocking` tinyint(1) NOT NULL,
    `RequiresVerification` tinyint(1) NOT NULL,
    `RequiresExpiryDate` tinyint(1) NOT NULL,
    `SortOrder` int NOT NULL,
    `Status` varchar(32) NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NULL,
    `UpdatedBy` varchar(191) NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `ClientEvidenceScanRoots` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `RootPath` varchar(512) NOT NULL,
    `IsActive` tinyint(1) NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NULL,
    `UpdatedBy` varchar(191) NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `ClientEvidenceScanRuns` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `RootPath` varchar(512) NOT NULL,
    `StartedAtUtc` datetime(6) NOT NULL,
    `FinishedAtUtc` datetime(6) NULL,
    `Status` varchar(32) NOT NULL,
    `TotalFiles` int NOT NULL,
    `LinkedFiles` int NOT NULL,
    `UnmatchedFiles` int NOT NULL,
    `AmbiguousFiles` int NOT NULL,
    `SkippedFiles` int NOT NULL,
    `ErrorMessage` longtext NULL,
    `StartedBy` varchar(191) NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `ClientEvidenceExceptions` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientId` int NOT NULL,
    `ClientEvidenceRequirementId` int NOT NULL,
    `Reason` longtext NOT NULL,
    `ApprovedBy` varchar(191) NOT NULL,
    `ApprovedAtUtc` datetime(6) NOT NULL,
    `ReviewDate` date NULL,
    `IsActive` tinyint(1) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientEvidenceExceptions_ClientEvidenceRequirements_ClientEv~` FOREIGN KEY (`ClientEvidenceRequirementId`) REFERENCES `ClientEvidenceRequirements` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_ClientEvidenceExceptions_Clients_ClientId` FOREIGN KEY (`ClientId`) REFERENCES `Clients` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `ClientEvidenceScanFiles` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientEvidenceScanRunId` int NOT NULL,
    `ClientId` int NULL,
    `FullPath` varchar(512) NOT NULL,
    `RelativePath` varchar(512) NOT NULL,
    `FileName` varchar(260) NOT NULL,
    `FileSha256` varchar(64) NOT NULL,
    `FileSizeBytes` bigint NOT NULL,
    `FileLastWriteTimeUtc` datetime(6) NOT NULL,
    `MatchStatus` varchar(32) NOT NULL,
    `SuggestedEvidenceType` varchar(96) NULL,
    `MatchReason` varchar(512) NULL,
    `CandidateCount` int NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientEvidenceScanFiles_ClientEvidenceScanRuns_ClientEvidenc~` FOREIGN KEY (`ClientEvidenceScanRunId`) REFERENCES `ClientEvidenceScanRuns` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_ClientEvidenceScanFiles_Clients_ClientId` FOREIGN KEY (`ClientId`) REFERENCES `Clients` (`Id`) ON DELETE SET NULL
);

CREATE TABLE `ClientEvidenceItems` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientId` int NOT NULL,
    `ClientEvidenceRequirementId` int NULL,
    `EvidenceType` varchar(96) NOT NULL,
    `Title` varchar(240) NOT NULL,
    `SourcePath` varchar(512) NULL,
    `RelativePath` varchar(512) NULL,
    `FileName` varchar(260) NULL,
    `FileSha256` varchar(64) NULL,
    `FileSizeBytes` bigint NULL,
    `FileLastWriteTimeUtc` datetime(6) NULL,
    `ReceivedDate` date NULL,
    `VerifiedDate` date NULL,
    `ExpiryDate` date NULL,
    `Reviewer` varchar(191) NULL,
    `Status` varchar(32) NOT NULL,
    `Notes` longtext NULL,
    `ClientEvidenceScanFileId` int NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NULL,
    `UpdatedBy` varchar(191) NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientEvidenceItems_ClientEvidenceRequirements_ClientEvidenc~` FOREIGN KEY (`ClientEvidenceRequirementId`) REFERENCES `ClientEvidenceRequirements` (`Id`) ON DELETE SET NULL,
    CONSTRAINT `FK_ClientEvidenceItems_ClientEvidenceScanFiles_ClientEvidenceSc~` FOREIGN KEY (`ClientEvidenceScanFileId`) REFERENCES `ClientEvidenceScanFiles` (`Id`) ON DELETE SET NULL,
    CONSTRAINT `FK_ClientEvidenceItems_Clients_ClientId` FOREIGN KEY (`ClientId`) REFERENCES `Clients` (`Id`) ON DELETE CASCADE
);

CREATE INDEX `IX_ClientEvidenceExceptions_ClientEvidenceRequirementId` ON `ClientEvidenceExceptions` (`ClientEvidenceRequirementId`);

CREATE INDEX `IX_ClientEvidenceExceptions_ClientId_ClientEvidenceRequirementI~` ON `ClientEvidenceExceptions` (`ClientId`, `ClientEvidenceRequirementId`, `IsActive`);

CREATE INDEX `IX_ClientEvidenceExceptions_ReviewDate` ON `ClientEvidenceExceptions` (`ReviewDate`);

CREATE INDEX `IX_ClientEvidenceItems_ClientEvidenceRequirementId` ON `ClientEvidenceItems` (`ClientEvidenceRequirementId`);

CREATE UNIQUE INDEX `IX_ClientEvidenceItems_ClientEvidenceScanFileId` ON `ClientEvidenceItems` (`ClientEvidenceScanFileId`);

CREATE INDEX `IX_ClientEvidenceItems_ClientId_EvidenceType_Status` ON `ClientEvidenceItems` (`ClientId`, `EvidenceType`, `Status`);

CREATE INDEX `IX_ClientEvidenceItems_ExpiryDate` ON `ClientEvidenceItems` (`ExpiryDate`);

CREATE INDEX `IX_ClientEvidenceItems_FileSha256` ON `ClientEvidenceItems` (`FileSha256`);

CREATE INDEX `IX_ClientEvidenceRequirements_ClientCategory_EvidenceType_Status` ON `ClientEvidenceRequirements` (`ClientCategory`, `EvidenceType`, `Status`);

CREATE INDEX `IX_ClientEvidenceRequirements_RequirementGroup_SortOrder` ON `ClientEvidenceRequirements` (`RequirementGroup`, `SortOrder`);

CREATE INDEX `IX_ClientEvidenceScanFiles_ClientEvidenceScanRunId_MatchStatus` ON `ClientEvidenceScanFiles` (`ClientEvidenceScanRunId`, `MatchStatus`);

CREATE INDEX `IX_ClientEvidenceScanFiles_ClientId` ON `ClientEvidenceScanFiles` (`ClientId`);

CREATE INDEX `IX_ClientEvidenceScanFiles_FileSha256` ON `ClientEvidenceScanFiles` (`FileSha256`);

CREATE INDEX `IX_ClientEvidenceScanRoots_IsActive` ON `ClientEvidenceScanRoots` (`IsActive`);

CREATE INDEX `IX_ClientEvidenceScanRuns_StartedAtUtc` ON `ClientEvidenceScanRuns` (`StartedAtUtc`);

CREATE INDEX `IX_ClientEvidenceScanRuns_Status` ON `ClientEvidenceScanRuns` (`Status`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260723125704_AddClientEvidenceReadiness', '10.0.10');

COMMIT;

