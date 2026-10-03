START TRANSACTION;
ALTER TABLE `Clients` ADD `RequiresClientAcceptance` tinyint(1) NOT NULL DEFAULT FALSE;

CREATE TABLE `ClientAcceptanceDecisions` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientId` int NOT NULL,
    `Decision` varchar(32) NOT NULL,
    `ContentHash` varchar(64) NOT NULL,
    `SnapshotJson` longtext NOT NULL,
    `Reason` longtext NOT NULL,
    `DecidedByUserId` varchar(64) NOT NULL,
    `DecidedBy` varchar(191) NOT NULL,
    `GovernanceRoleAssignmentId` int NOT NULL,
    `DecidedAtUtc` datetime(6) NOT NULL,
    `ImportSourceReference` longtext NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientAcceptanceDecisions_Clients_ClientId` FOREIGN KEY (`ClientId`) REFERENCES `Clients` (`Id`) ON DELETE RESTRICT
);

CREATE TABLE `ClientCodexReviewRequests` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientId` int NOT NULL,
    `ComplianceTaskId` int NOT NULL,
    `MaterialHash` varchar(64) NOT NULL,
    `Status` varchar(32) NOT NULL,
    `Brief` longtext NOT NULL,
    `RecipientUserIdsJson` longtext NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `CompletedAtUtc` datetime(6) NULL,
    `CompletedContentHash` varchar(64) NULL,
    `CompletionSummary` longtext NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientCodexReviewRequests_Clients_ClientId` FOREIGN KEY (`ClientId`) REFERENCES `Clients` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_ClientCodexReviewRequests_ComplianceTasks_ComplianceTaskId` FOREIGN KEY (`ComplianceTaskId`) REFERENCES `ComplianceTasks` (`Id`) ON DELETE RESTRICT
);

CREATE TABLE `ClientOnboardingProfiles` (
    `ClientId` int NOT NULL,
    `Version` varchar(64) NOT NULL,
    `RequestedService` longtext NOT NULL,
    `ResponsibleRepresentative` longtext NOT NULL,
    `PurposeAndProposedFunds` longtext NOT NULL,
    `DisclosureVersion` longtext NOT NULL,
    `DisclosureDeliveredAtUtc` datetime(6) NULL,
    `DisclosureDeliveryReference` longtext NOT NULL,
    `EnhancedMeasures` longtext NOT NULL,
    `RelationshipCommencedAtUtc` datetime(6) NULL,
    `RelationshipAuthorityReference` longtext NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    `UpdatedBy` varchar(191) NOT NULL,
    PRIMARY KEY (`ClientId`),
    CONSTRAINT `FK_ClientOnboardingProfiles_Clients_ClientId` FOREIGN KEY (`ClientId`) REFERENCES `Clients` (`Id`) ON DELETE RESTRICT
);

CREATE INDEX `IX_ClientAcceptanceDecisions_ClientId_DecidedAtUtc` ON `ClientAcceptanceDecisions` (`ClientId`, `DecidedAtUtc`);

CREATE INDEX `IX_ClientCodexReviewRequests_ClientId_Status` ON `ClientCodexReviewRequests` (`ClientId`, `Status`);

CREATE INDEX `IX_ClientCodexReviewRequests_ComplianceTaskId` ON `ClientCodexReviewRequests` (`ComplianceTaskId`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261003063950_AddClientOnboarding', '10.0.10');

COMMIT;
