START TRANSACTION;
ALTER TABLE `ComplianceTasks` ADD `BusinessRiskAssessmentId` int NULL;

ALTER TABLE `ComplianceTasks` ADD `ClientId` int NULL;

ALTER TABLE `ComplianceTasks` ADD `ClientRiskAssessmentId` int NULL;

ALTER TABLE `ComplianceTasks` ADD `ClosedBy` varchar(191) NULL;

ALTER TABLE `ComplianceTasks` ADD `ClosureReason` longtext NULL;

ALTER TABLE `ComplianceTasks` ADD `ClosureRequestedAtUtc` datetime(6) NULL;

ALTER TABLE `ComplianceTasks` ADD `ClosureRequestedBy` varchar(191) NULL;

ALTER TABLE `ComplianceTasks` ADD `EscalatedAtUtc` datetime(6) NULL;

ALTER TABLE `ComplianceTasks` ADD `EscalatedBy` varchar(191) NULL;

ALTER TABLE `ComplianceTasks` ADD `EvidenceSummary` longtext NULL;

ALTER TABLE `ComplianceTasks` ADD `Outcome` longtext NULL;

ALTER TABLE `ComplianceTasks` ADD `RmcpControlId` int NULL;

ALTER TABLE `ComplianceTasks` ADD `RmcpVersionId` int NULL;

ALTER TABLE `ComplianceTasks` ADD `TaskType` varchar(48) NOT NULL DEFAULT 'Remediation';

CREATE INDEX `IX_ComplianceTasks_BusinessRiskAssessmentId` ON `ComplianceTasks` (`BusinessRiskAssessmentId`);

CREATE INDEX `IX_ComplianceTasks_ClientId` ON `ComplianceTasks` (`ClientId`);

CREATE INDEX `IX_ComplianceTasks_ClientRiskAssessmentId` ON `ComplianceTasks` (`ClientRiskAssessmentId`);

CREATE INDEX `IX_ComplianceTasks_RmcpControlId` ON `ComplianceTasks` (`RmcpControlId`);

CREATE INDEX `IX_ComplianceTasks_RmcpVersionId` ON `ComplianceTasks` (`RmcpVersionId`);

CREATE INDEX `IX_ComplianceTasks_TaskType_Status_DueDate` ON `ComplianceTasks` (`TaskType`, `Status`, `DueDate`);

ALTER TABLE `ComplianceTasks` ADD CONSTRAINT `FK_ComplianceTasks_BusinessRiskAssessments_BusinessRiskAssessme~` FOREIGN KEY (`BusinessRiskAssessmentId`) REFERENCES `BusinessRiskAssessments` (`Id`) ON DELETE RESTRICT;

ALTER TABLE `ComplianceTasks` ADD CONSTRAINT `FK_ComplianceTasks_ClientRiskAssessments_ClientRiskAssessmentId` FOREIGN KEY (`ClientRiskAssessmentId`) REFERENCES `ClientRiskAssessments` (`Id`) ON DELETE RESTRICT;

ALTER TABLE `ComplianceTasks` ADD CONSTRAINT `FK_ComplianceTasks_Clients_ClientId` FOREIGN KEY (`ClientId`) REFERENCES `Clients` (`Id`) ON DELETE RESTRICT;

ALTER TABLE `ComplianceTasks` ADD CONSTRAINT `FK_ComplianceTasks_RmcpControls_RmcpControlId` FOREIGN KEY (`RmcpControlId`) REFERENCES `RmcpControls` (`Id`) ON DELETE RESTRICT;

ALTER TABLE `ComplianceTasks` ADD CONSTRAINT `FK_ComplianceTasks_RmcpVersions_RmcpVersionId` FOREIGN KEY (`RmcpVersionId`) REFERENCES `RmcpVersions` (`Id`) ON DELETE RESTRICT;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260726170142_AddComplianceWorkRegister', '10.0.10');

COMMIT;

