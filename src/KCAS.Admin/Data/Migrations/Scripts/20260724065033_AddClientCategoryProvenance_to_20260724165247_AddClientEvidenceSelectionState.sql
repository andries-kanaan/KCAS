START TRANSACTION;
ALTER TABLE `ClientEvidenceItems` ADD `SelectedAtUtc` datetime(6) NULL;

ALTER TABLE `ClientEvidenceItems` ADD `SelectedBy` varchar(191) NULL;

ALTER TABLE `ClientEvidenceItems` ADD `SelectionConfidence` int NULL;

ALTER TABLE `ClientEvidenceItems` ADD `SelectionReason` varchar(512) NULL;

ALTER TABLE `ClientEvidenceItems` ADD `SelectionStatus` varchar(32) NOT NULL DEFAULT 'Candidate';

ALTER TABLE `ClientEvidenceItems` ADD `SupersededByClientEvidenceItemId` int NULL;

ALTER TABLE `ClientEvidenceItems` ADD `VerificationPolicy` varchar(32) NOT NULL DEFAULT 'ManualRequired';

CREATE INDEX `IX_ClientEvidenceItems_ClientId_EvidenceType_SelectionStatus` ON `ClientEvidenceItems` (`ClientId`, `EvidenceType`, `SelectionStatus`);

CREATE INDEX `IX_ClientEvidenceItems_SupersededByClientEvidenceItemId` ON `ClientEvidenceItems` (`SupersededByClientEvidenceItemId`);

ALTER TABLE `ClientEvidenceItems` ADD CONSTRAINT `FK_CEI_SupersededBy` FOREIGN KEY (`SupersededByClientEvidenceItemId`) REFERENCES `ClientEvidenceItems` (`Id`) ON DELETE SET NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260724165247_AddClientEvidenceSelectionState', '10.0.10');

COMMIT;

