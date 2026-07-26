START TRANSACTION;
CREATE TABLE `InspectionCases` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Reference` varchar(96) NOT NULL,
    `Title` varchar(240) NOT NULL,
    `RequestingAuthority` varchar(191) NOT NULL,
    `AsAtDate` date NOT NULL,
    `RequestDate` date NOT NULL,
    `DueDate` date NOT NULL,
    `Status` varchar(32) NOT NULL,
    `Scope` longtext NOT NULL,
    `Coordinator` varchar(191) NOT NULL,
    `Notes` longtext NULL,
    `SnapshotJson` longtext NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    `FrozenAtUtc` datetime(6) NULL,
    `CreatedBy` varchar(191) NULL,
    `UpdatedBy` varchar(191) NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `InspectionReadinessChecks` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `InspectionCaseId` int NOT NULL,
    `CheckType` varchar(64) NOT NULL,
    `Status` varchar(32) NOT NULL,
    `EvidenceLocation` longtext NULL,
    `Notes` longtext NULL,
    `TestedAtUtc` datetime(6) NULL,
    `TestedBy` varchar(191) NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_InspectionReadinessChecks_InspectionCases_InspectionCaseId` FOREIGN KEY (`InspectionCaseId`) REFERENCES `InspectionCases` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `InspectionRequestItems` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `InspectionCaseId` int NOT NULL,
    `Category` varchar(64) NOT NULL,
    `Title` varchar(240) NOT NULL,
    `Description` longtext NULL,
    `Owner` varchar(191) NOT NULL,
    `DueDate` date NOT NULL,
    `Status` varchar(32) NOT NULL,
    `EvidenceTitle` longtext NULL,
    `EvidenceLocation` longtext NULL,
    `LinkedEntityType` varchar(128) NULL,
    `LinkedEntityId` int NULL,
    `ReviewNotes` longtext NULL,
    `CompletedAtUtc` datetime(6) NULL,
    `CompletedBy` varchar(191) NULL,
    `SortOrder` int NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_InspectionRequestItems_InspectionCases_InspectionCaseId` FOREIGN KEY (`InspectionCaseId`) REFERENCES `InspectionCases` (`Id`) ON DELETE CASCADE
);

CREATE INDEX `IX_InspectionCases_Status_DueDate` ON `InspectionCases` (`Status`, `DueDate`);

CREATE UNIQUE INDEX `IX_InspectionReadinessChecks_InspectionCaseId_CheckType` ON `InspectionReadinessChecks` (`InspectionCaseId`, `CheckType`);

CREATE INDEX `IX_InspectionRequestItems_InspectionCaseId_Status_DueDate` ON `InspectionRequestItems` (`InspectionCaseId`, `Status`, `DueDate`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260726173018_AddInspectionReadiness', '10.0.10');

COMMIT;

