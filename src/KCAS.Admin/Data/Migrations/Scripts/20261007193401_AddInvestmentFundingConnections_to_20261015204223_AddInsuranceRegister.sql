START TRANSACTION;
ALTER TABLE `ComplianceTasks` ADD `InsurancePolicyId` int NULL;

CREATE TABLE `InsurancePolicies` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `TransferKey` char(36) NOT NULL,
    `Version` varchar(32) NOT NULL,
    `InsuredName` varchar(240) NOT NULL,
    `PolicyNumber` varchar(120) NOT NULL,
    `Insurer` varchar(240) NOT NULL,
    `Broker` varchar(240) NOT NULL,
    `BrokerContact` varchar(500) NOT NULL,
    `PrincipalAddress` varchar(500) NOT NULL,
    `BusinessDescription` varchar(2000) NOT NULL,
    `StartDate` date NOT NULL,
    `EndDate` date NOT NULL,
    `RetroactiveDate` date NULL,
    `Limit` decimal(18,2) NOT NULL,
    `CombinedLimit` tinyint(1) NOT NULL,
    `Excess` decimal(18,2) NOT NULL,
    `PremiumExVat` decimal(18,2) NOT NULL,
    `Vat` decimal(18,2) NOT NULL,
    `MonthlyInstalment` decimal(18,2) NULL,
    `PaymentFrequency` varchar(32) NOT NULL,
    `CoverSummary` longtext NOT NULL,
    `Exclusions` longtext NOT NULL,
    `Findings` longtext NOT NULL,
    `Owner` varchar(500) NOT NULL,
    `ReminderDays` int NOT NULL,
    `FollowUpDays` int NOT NULL,
    `UrgentDays` int NOT NULL,
    `Status` varchar(32) NOT NULL,
    `RequestedVersion` varchar(32) NULL,
    `ReviewedVersion` varchar(32) NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    `UpdatedBy` varchar(191) NOT NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `InsuranceDocuments` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `InsurancePolicyId` int NOT NULL,
    `TransferKey` char(36) NOT NULL,
    `Kind` varchar(64) NOT NULL,
    `Title` varchar(240) NOT NULL,
    `RelativePath` varchar(2000) NOT NULL,
    `Sha256` varchar(64) NOT NULL,
    `DocumentDate` date NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_InsuranceDocuments_InsurancePolicies_InsurancePolicyId` FOREIGN KEY (`InsurancePolicyId`) REFERENCES `InsurancePolicies` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `InsuranceEvents` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `InsurancePolicyId` int NOT NULL,
    `TransferKey` char(36) NOT NULL,
    `Action` varchar(64) NOT NULL,
    `Performer` varchar(191) NOT NULL,
    `RecordedBy` varchar(191) NOT NULL,
    `RecordedAtUtc` datetime(6) NOT NULL,
    `OccurredOn` date NOT NULL,
    `Summary` longtext NOT NULL,
    `DocumentKey` char(36) NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_InsuranceEvents_InsurancePolicies_InsurancePolicyId` FOREIGN KEY (`InsurancePolicyId`) REFERENCES `InsurancePolicies` (`Id`) ON DELETE CASCADE
);

CREATE UNIQUE INDEX `IX_ComplianceTasks_InsurancePolicyId` ON `ComplianceTasks` (`InsurancePolicyId`);

CREATE INDEX `IX_InsuranceDocuments_InsurancePolicyId` ON `InsuranceDocuments` (`InsurancePolicyId`);

CREATE UNIQUE INDEX `IX_InsuranceDocuments_TransferKey` ON `InsuranceDocuments` (`TransferKey`);

CREATE INDEX `IX_InsuranceEvents_InsurancePolicyId` ON `InsuranceEvents` (`InsurancePolicyId`);

CREATE UNIQUE INDEX `IX_InsuranceEvents_TransferKey` ON `InsuranceEvents` (`TransferKey`);

CREATE UNIQUE INDEX `IX_InsurancePolicies_PolicyNumber_StartDate` ON `InsurancePolicies` (`PolicyNumber`, `StartDate`);

CREATE UNIQUE INDEX `IX_InsurancePolicies_TransferKey` ON `InsurancePolicies` (`TransferKey`);

ALTER TABLE `ComplianceTasks` ADD CONSTRAINT `FK_ComplianceTasks_InsurancePolicies_InsurancePolicyId` FOREIGN KEY (`InsurancePolicyId`) REFERENCES `InsurancePolicies` (`Id`) ON DELETE RESTRICT;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261015204223_AddInsuranceRegister', '10.0.10');

COMMIT;

