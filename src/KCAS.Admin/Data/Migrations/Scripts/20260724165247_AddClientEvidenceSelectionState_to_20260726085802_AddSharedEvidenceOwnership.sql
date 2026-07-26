START TRANSACTION;
ALTER TABLE `ClientEvidenceItems` ADD `OwnershipConfidence` int NULL;

ALTER TABLE `ClientEvidenceItems` ADD `OwnershipReason` varchar(512) NULL;

ALTER TABLE `ClientEvidenceItems` ADD `OwnershipReviewedAtUtc` datetime(6) NULL;

ALTER TABLE `ClientEvidenceItems` ADD `OwnershipReviewedBy` varchar(191) NULL;

ALTER TABLE `ClientEvidenceItems` ADD `OwnershipStatus` varchar(32) NOT NULL DEFAULT 'Confirmed';

CREATE TABLE `ClientEvidenceOwnershipAliases` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientId` int NOT NULL,
    `FolderPath` varchar(512) NOT NULL,
    `Alias` varchar(160) NOT NULL,
    `IsJoint` tinyint(1) NOT NULL,
    `IsActive` tinyint(1) NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `CreatedBy` varchar(191) NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientEvidenceOwnershipAliases_Clients_ClientId` FOREIGN KEY (`ClientId`) REFERENCES `Clients` (`Id`) ON DELETE CASCADE
);

CREATE INDEX `IX_ClientEvidenceItems_ClientId_OwnershipStatus` ON `ClientEvidenceItems` (`ClientId`, `OwnershipStatus`);

CREATE UNIQUE INDEX `IX_ClientEvidenceOwnershipAliases_ClientId_FolderPath_Alias` ON `ClientEvidenceOwnershipAliases` (`ClientId`, `FolderPath`, `Alias`);

CREATE INDEX `IX_ClientEvidenceOwnershipAliases_FolderPath_IsActive` ON `ClientEvidenceOwnershipAliases` (`FolderPath`, `IsActive`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260726085802_AddSharedEvidenceOwnership', '10.0.10');

COMMIT;
