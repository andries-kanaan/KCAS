START TRANSACTION;
ALTER TABLE `ClientEvidenceItems` ADD `EscalationRequired` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `ClientEvidenceItems` ADD `ScreeningOutcome` varchar(96) NULL;

ALTER TABLE `ClientEvidenceItems` ADD `ScreeningReviewDate` date NULL;

ALTER TABLE `ClientEvidenceItems` ADD `ScreeningRiskSignal` varchar(32) NULL;

ALTER TABLE `ClientEvidenceItems` ADD `ScreeningSubjectName` varchar(240) NULL;

ALTER TABLE `ClientEvidenceItems` ADD `ScreeningSubjectType` varchar(96) NULL;

CREATE INDEX `IX_ClientEvidenceItems_ClientId_EvidenceType_ScreeningRiskSignal` ON `ClientEvidenceItems` (`ClientId`, `EvidenceType`, `ScreeningRiskSignal`);

CREATE INDEX `IX_ClientEvidenceItems_EscalationRequired` ON `ClientEvidenceItems` (`EscalationRequired`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260723191721_AddClientEvidenceScreeningReviews', '10.0.10');

COMMIT;

