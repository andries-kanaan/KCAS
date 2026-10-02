START TRANSACTION;
CREATE TABLE `EmployeeProfiles` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `DisplayName` varchar(191) NOT NULL,
    `LegalName` varchar(191) NOT NULL,
    `Aliases` longtext NOT NULL,
    `Email` varchar(191) NULL,
    `EmploymentStatus` varchar(32) NOT NULL,
    `IdentityReference` longtext NOT NULL,
    `Responsibilities` longtext NOT NULL,
    `AuthorityLimits` longtext NOT NULL,
    `SourceReference` longtext NOT NULL,
    `RoleExposure` varchar(32) NOT NULL,
    `RiskRationale` longtext NOT NULL,
    `SelectedChecks` longtext NOT NULL,
    `RequireTraining` tinyint(1) NOT NULL,
    `RequireRegulatedCompetence` tinyint(1) NOT NULL,
    `RequireAdditionalCheck` tinyint(1) NOT NULL,
    `ProposedReviewMonths` int NOT NULL,
    `EmploymentStart` date NULL,
    `EmploymentEnd` date NULL,
    `ExternalAccessScope` longtext NOT NULL,
    `Version` varchar(64) NOT NULL,
    `CreatedByUserId` varchar(64) NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `EmployeeTfsBatches` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `SourceVersion` varchar(191) NOT NULL,
    `SourceUrl` varchar(1024) NOT NULL,
    `Reason` longtext NOT NULL,
    `CreatedByUserId` varchar(64) NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `EmployeeAccountLinks` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `EmployeeProfileId` int NOT NULL,
    `UserId` varchar(64) NOT NULL,
    `VerificationReference` longtext NOT NULL,
    `LinkedByUserId` varchar(64) NOT NULL,
    `LinkedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_EmployeeAccountLinks_AspNetUsers_UserId` FOREIGN KEY (`UserId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_EmployeeAccountLinks_EmployeeProfiles_EmployeeProfileId` FOREIGN KEY (`EmployeeProfileId`) REFERENCES `EmployeeProfiles` (`Id`) ON DELETE RESTRICT
);

CREATE TABLE `EmployeeComplianceAuditEvents` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `EmployeeProfileId` int NOT NULL,
    `Action` varchar(48) NOT NULL,
    `UserId` varchar(64) NOT NULL,
    `TimestampUtc` datetime(6) NOT NULL,
    `Reason` longtext NOT NULL,
    `SnapshotJson` longtext NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_EmployeeComplianceAuditEvents_EmployeeProfiles_EmployeeProfi~` FOREIGN KEY (`EmployeeProfileId`) REFERENCES `EmployeeProfiles` (`Id`) ON DELETE RESTRICT
);

CREATE TABLE `EmployeeComplianceReviews` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `EmployeeProfileId` int NOT NULL,
    `Status` varchar(32) NOT NULL,
    `ProfileVersion` varchar(64) NOT NULL,
    `Version` varchar(64) NOT NULL,
    `ProfileSnapshotJson` longtext NOT NULL,
    `PreparedByUserId` varchar(64) NOT NULL,
    `StartedAtUtc` datetime(6) NOT NULL,
    `CompletedAtUtc` datetime(6) NULL,
    `NextReviewDate` date NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_EmployeeComplianceReviews_EmployeeProfiles_EmployeeProfileId` FOREIGN KEY (`EmployeeProfileId`) REFERENCES `EmployeeProfiles` (`Id`) ON DELETE RESTRICT
);

CREATE TABLE `EmployeeEvidenceDocuments` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `EmployeeProfileId` int NOT NULL,
    `Title` varchar(191) NOT NULL,
    `Category` varchar(48) NOT NULL,
    `EvidencePath` varchar(1024) NOT NULL,
    `EvidenceSha256` varchar(64) NOT NULL,
    `SourceNote` longtext NOT NULL,
    `LinkedByUserId` varchar(64) NOT NULL,
    `LinkedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_EmployeeEvidenceDocuments_EmployeeProfiles_EmployeeProfileId` FOREIGN KEY (`EmployeeProfileId`) REFERENCES `EmployeeProfiles` (`Id`) ON DELETE RESTRICT
);

CREATE TABLE `EmployeeComplianceTasks` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `EmployeeProfileId` int NOT NULL,
    `TriggerKey` varchar(191) NOT NULL,
    `Kind` varchar(48) NOT NULL,
    `Status` varchar(32) NOT NULL,
    `Reason` longtext NOT NULL,
    `DueDate` date NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `RecipientUserIdsJson` longtext NOT NULL,
    `AcknowledgedByUserId` varchar(64) NULL,
    `AcknowledgedAtUtc` datetime(6) NULL,
    `ClosedAtUtc` datetime(6) NULL,
    `EmployeeTfsBatchId` int NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_EmployeeComplianceTasks_EmployeeProfiles_EmployeeProfileId` FOREIGN KEY (`EmployeeProfileId`) REFERENCES `EmployeeProfiles` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_EmployeeComplianceTasks_EmployeeTfsBatches_EmployeeTfsBatchId` FOREIGN KEY (`EmployeeTfsBatchId`) REFERENCES `EmployeeTfsBatches` (`Id`) ON DELETE RESTRICT
);

CREATE TABLE `EmployeeAccessConfirmations` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `EmployeeComplianceReviewId` int NOT NULL,
    `Kind` varchar(32) NOT NULL,
    `SystemAndScope` longtext NOT NULL,
    `ApprovedScope` longtext NOT NULL,
    `ActualScope` longtext NOT NULL,
    `ActionConfirmation` longtext NOT NULL,
    `IsAligned` tinyint(1) NOT NULL,
    `VerifiedAtUtc` datetime(6) NOT NULL,
    `VerifiedBy` varchar(191) NOT NULL,
    `EvidenceReference` longtext NOT NULL,
    `AccountScopeJson` longtext NOT NULL,
    `RecordedByUserId` varchar(64) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_EmployeeAccessConfirmations_EmployeeComplianceReviews_Employ~` FOREIGN KEY (`EmployeeComplianceReviewId`) REFERENCES `EmployeeComplianceReviews` (`Id`) ON DELETE RESTRICT
);

CREATE TABLE `EmployeeComplianceChecks` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `EmployeeComplianceReviewId` int NOT NULL,
    `Kind` varchar(48) NOT NULL,
    `Outcome` varchar(32) NOT NULL,
    `PerformerType` varchar(32) NOT NULL,
    `Performer` varchar(191) NOT NULL,
    `PerformedAtUtc` datetime(6) NOT NULL,
    `SourceReference` longtext NOT NULL,
    `SourceUrl` varchar(1024) NULL,
    `ListVersion` varchar(191) NULL,
    `IdentifierScope` longtext NOT NULL,
    `Finding` longtext NOT NULL,
    `Limitations` longtext NOT NULL,
    `EvidencePath` varchar(1024) NULL,
    `EvidenceSha256` varchar(64) NULL,
    `SupersedesCheckId` int NULL,
    `EmployeeTfsBatchId` int NULL,
    `RecordedByUserId` varchar(64) NOT NULL,
    `RecordedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_EmployeeComplianceChecks_EmployeeComplianceChecks_Supersedes~` FOREIGN KEY (`SupersedesCheckId`) REFERENCES `EmployeeComplianceChecks` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_EmployeeComplianceChecks_EmployeeComplianceReviews_EmployeeC~` FOREIGN KEY (`EmployeeComplianceReviewId`) REFERENCES `EmployeeComplianceReviews` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_EmployeeComplianceChecks_EmployeeTfsBatches_EmployeeTfsBatch~` FOREIGN KEY (`EmployeeTfsBatchId`) REFERENCES `EmployeeTfsBatches` (`Id`) ON DELETE RESTRICT
);

CREATE TABLE `EmployeeReviewDecisions` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `EmployeeComplianceReviewId` int NOT NULL,
    `Decision` varchar(32) NOT NULL,
    `Reason` longtext NOT NULL,
    `RestrictionsAndFollowUp` longtext NOT NULL,
    `ApprovedReviewMonths` int NULL,
    `ReviewerUserId` varchar(64) NOT NULL,
    `ReviewerName` varchar(191) NOT NULL,
    `ReviewerEmployeeProfileId` int NOT NULL,
    `DecidedAtUtc` datetime(6) NOT NULL,
    `EvidenceSnapshotJson` longtext NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_EmployeeReviewDecisions_EmployeeComplianceReviews_EmployeeCo~` FOREIGN KEY (`EmployeeComplianceReviewId`) REFERENCES `EmployeeComplianceReviews` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_EmployeeReviewDecisions_EmployeeProfiles_ReviewerEmployeePro~` FOREIGN KEY (`ReviewerEmployeeProfileId`) REFERENCES `EmployeeProfiles` (`Id`) ON DELETE RESTRICT
);

CREATE INDEX `IX_EmployeeAccessConfirmations_EmployeeComplianceReviewId` ON `EmployeeAccessConfirmations` (`EmployeeComplianceReviewId`);

CREATE INDEX `IX_EmployeeAccountLinks_EmployeeProfileId` ON `EmployeeAccountLinks` (`EmployeeProfileId`);

CREATE UNIQUE INDEX `IX_EmployeeAccountLinks_UserId` ON `EmployeeAccountLinks` (`UserId`);

CREATE INDEX `IX_EmployeeComplianceAuditEvents_EmployeeProfileId_TimestampUtc` ON `EmployeeComplianceAuditEvents` (`EmployeeProfileId`, `TimestampUtc`);

CREATE INDEX `IX_EmployeeComplianceChecks_EmployeeComplianceReviewId_Kind` ON `EmployeeComplianceChecks` (`EmployeeComplianceReviewId`, `Kind`);

CREATE INDEX `IX_EmployeeComplianceChecks_EmployeeTfsBatchId` ON `EmployeeComplianceChecks` (`EmployeeTfsBatchId`);

CREATE INDEX `IX_EmployeeComplianceChecks_SupersedesCheckId` ON `EmployeeComplianceChecks` (`SupersedesCheckId`);

CREATE INDEX `IX_EmployeeComplianceReviews_EmployeeProfileId_Status` ON `EmployeeComplianceReviews` (`EmployeeProfileId`, `Status`);

CREATE UNIQUE INDEX `IX_EmployeeComplianceTasks_EmployeeProfileId_TriggerKey` ON `EmployeeComplianceTasks` (`EmployeeProfileId`, `TriggerKey`);

CREATE INDEX `IX_EmployeeComplianceTasks_EmployeeTfsBatchId` ON `EmployeeComplianceTasks` (`EmployeeTfsBatchId`);

CREATE INDEX `IX_EmployeeEvidenceDocuments_EmployeeProfileId_EvidenceSha256` ON `EmployeeEvidenceDocuments` (`EmployeeProfileId`, `EvidenceSha256`);

CREATE INDEX `IX_EmployeeProfiles_EmploymentStatus_DisplayName` ON `EmployeeProfiles` (`EmploymentStatus`, `DisplayName`);

CREATE UNIQUE INDEX `IX_EmployeeReviewDecisions_EmployeeComplianceReviewId` ON `EmployeeReviewDecisions` (`EmployeeComplianceReviewId`);

CREATE INDEX `IX_EmployeeReviewDecisions_ReviewerEmployeeProfileId` ON `EmployeeReviewDecisions` (`ReviewerEmployeeProfileId`);

CREATE UNIQUE INDEX `IX_EmployeeTfsBatches_SourceVersion` ON `EmployeeTfsBatches` (`SourceVersion`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261002105448_AddEmployeeCompliance', '10.0.10');

COMMIT;
