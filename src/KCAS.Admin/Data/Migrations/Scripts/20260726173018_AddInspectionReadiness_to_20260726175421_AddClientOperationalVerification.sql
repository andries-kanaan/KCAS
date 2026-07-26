START TRANSACTION;
ALTER TABLE `Clients` ADD `DuplicateOfClientId` int NULL;

ALTER TABLE `Clients` ADD `LifecycleReason` varchar(1000) NULL;

ALTER TABLE `Clients` ADD `LifecycleReviewedAtUtc` datetime(6) NULL;

ALTER TABLE `Clients` ADD `LifecycleReviewedBy` varchar(191) NULL;

ALTER TABLE `Clients` ADD `LifecycleStatus` varchar(32) NOT NULL DEFAULT 'Unreviewed';

CREATE TABLE `ClientVerificationItems` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientId` int NOT NULL,
    `FieldCode` varchar(64) NOT NULL,
    `FieldLabel` varchar(191) NOT NULL,
    `ChangeType` varchar(32) NOT NULL,
    `ExistingValue` longtext NULL,
    `ProposedValue` longtext NULL,
    `SourceReference` varchar(1024) NOT NULL,
    `Recommendation` longtext NULL,
    `Status` varchar(32) NOT NULL,
    `IsBlocking` tinyint(1) NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `CreatedBy` varchar(191) NOT NULL,
    `DecidedAtUtc` datetime(6) NULL,
    `DecidedBy` varchar(191) NULL,
    `DecisionReason` varchar(1000) NULL,
    `AppliedAtUtc` datetime(6) NULL,
    `AppliedBy` varchar(191) NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientVerificationItems_Clients_ClientId` FOREIGN KEY (`ClientId`) REFERENCES `Clients` (`Id`) ON DELETE CASCADE
);

CREATE INDEX `IX_Clients_DuplicateOfClientId` ON `Clients` (`DuplicateOfClientId`);

CREATE INDEX `IX_Clients_LifecycleStatus` ON `Clients` (`LifecycleStatus`);

CREATE INDEX `IX_ClientVerificationItems_ClientId_Status_IsBlocking` ON `ClientVerificationItems` (`ClientId`, `Status`, `IsBlocking`);

ALTER TABLE `Clients` ADD CONSTRAINT `FK_Clients_Clients_DuplicateOfClientId` FOREIGN KEY (`DuplicateOfClientId`) REFERENCES `Clients` (`Id`) ON DELETE RESTRICT;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260726175421_AddClientOperationalVerification', '10.0.10');

COMMIT;

