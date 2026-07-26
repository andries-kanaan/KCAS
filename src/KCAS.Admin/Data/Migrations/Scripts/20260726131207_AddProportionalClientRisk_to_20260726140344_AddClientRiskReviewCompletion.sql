START TRANSACTION;
ALTER TABLE `ClientRiskAssessments` ADD `PreviousAssessmentId` int NULL;

ALTER TABLE `ClientRiskAssessments` ADD `ReviewTriggerReason` longtext NULL;

ALTER TABLE `ClientRiskAssessments` ADD `ReviewTriggerType` varchar(48) NOT NULL DEFAULT 'Initial';

ALTER TABLE `ClientRiskAssessments` ADD `ReviewTriggeredAtUtc` datetime(6) NULL;

ALTER TABLE `ClientRiskAssessments` ADD `ReviewTriggeredBy` varchar(191) NULL;

ALTER TABLE `ClientRiskAssessmentResponses` ADD `ConfirmedAtUtc` datetime(6) NULL;

ALTER TABLE `ClientRiskAssessmentResponses` ADD `ConfirmedBy` varchar(191) NULL;

CREATE INDEX `IX_ClientRiskAssessments_PreviousAssessmentId` ON `ClientRiskAssessments` (`PreviousAssessmentId`);

ALTER TABLE `ClientRiskAssessments` ADD CONSTRAINT `FK_ClientRiskAssessments_ClientRiskAssessments_PreviousAssessme~` FOREIGN KEY (`PreviousAssessmentId`) REFERENCES `ClientRiskAssessments` (`Id`) ON DELETE RESTRICT;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260726140344_AddClientRiskReviewCompletion', '10.0.10');

COMMIT;

