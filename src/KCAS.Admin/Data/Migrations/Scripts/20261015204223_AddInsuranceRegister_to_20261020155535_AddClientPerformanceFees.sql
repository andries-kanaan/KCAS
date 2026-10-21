START TRANSACTION;
CREATE TABLE `PerformanceFeeProfiles` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientInvestmentAccountId` int NOT NULL,
    `Version` varchar(32) NOT NULL,
    `InceptionDate` date NULL,
    `NextReviewDate` date NULL,
    `PreparationJson` longtext NOT NULL,
    `CheckpointJson` longtext NULL,
    `CodexRequestVersion` varchar(32) NULL,
    `UpdatedBy` varchar(191) NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_PerformanceFeeProfiles_ClientInvestmentAccounts_ClientInvest~` FOREIGN KEY (`ClientInvestmentAccountId`) REFERENCES `ClientInvestmentAccounts` (`Id`) ON DELETE RESTRICT
);

CREATE TABLE `PerformanceFeeClaims` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `PerformanceFeeProfileId` int NOT NULL,
    `Reference` varchar(40) NOT NULL,
    `Version` varchar(32) NOT NULL,
    `Status` varchar(32) NOT NULL,
    `From` date NOT NULL,
    `To` date NOT NULL,
    `InputJson` longtext NOT NULL,
    `ResultJson` longtext NOT NULL,
    `PreparedBy` varchar(191) NOT NULL,
    `Performer` varchar(32) NOT NULL,
    `PreparedAtUtc` datetime(6) NOT NULL,
    `ApprovedBy` varchar(191) NULL,
    `ApprovedAtUtc` datetime(6) NULL,
    `Paid` decimal(18,2) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_PerformanceFeeClaims_PerformanceFeeProfiles_PerformanceFeePr~` FOREIGN KEY (`PerformanceFeeProfileId`) REFERENCES `PerformanceFeeProfiles` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `PerformanceFeeEvents` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `PerformanceFeeProfileId` int NOT NULL,
    `ClaimId` int NULL,
    `Action` varchar(64) NOT NULL,
    `Performer` varchar(32) NOT NULL,
    `RecordedBy` varchar(191) NOT NULL,
    `RecordedAtUtc` datetime(6) NOT NULL,
    `OccurredOn` date NOT NULL,
    `Reference` longtext NOT NULL,
    `Summary` longtext NOT NULL,
    `Amount` decimal(18,2) NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_PerformanceFeeEvents_PerformanceFeeProfiles_PerformanceFeePr~` FOREIGN KEY (`PerformanceFeeProfileId`) REFERENCES `PerformanceFeeProfiles` (`Id`) ON DELETE CASCADE
);

CREATE INDEX `IX_PerformanceFeeClaims_PerformanceFeeProfileId_From_To` ON `PerformanceFeeClaims` (`PerformanceFeeProfileId`, `From`, `To`);

CREATE UNIQUE INDEX `IX_PerformanceFeeClaims_Reference` ON `PerformanceFeeClaims` (`Reference`);

CREATE INDEX `IX_PerformanceFeeEvents_PerformanceFeeProfileId` ON `PerformanceFeeEvents` (`PerformanceFeeProfileId`);

CREATE UNIQUE INDEX `IX_PerformanceFeeProfiles_ClientInvestmentAccountId` ON `PerformanceFeeProfiles` (`ClientInvestmentAccountId`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261020155535_AddClientPerformanceFees', '10.0.10');

COMMIT;
