START TRANSACTION;

CREATE TABLE `InvestmentAdministratorReferences` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `LegacyLispId` int NULL,
    `Name` varchar(256) NOT NULL,
    `ShortName` varchar(256) NULL,
    `IsCurrent` tinyint(1) NOT NULL,
    `MonthlyUpload` tinyint(1) NOT NULL,
    `OpenedBy` varchar(256) NULL,
    `UpdatedBy` varchar(256) NULL,
    `LegacyOpenedAt` datetime(6) NULL,
    `LegacyUpdatedAt` datetime(6) NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `InvestmentFundReferences` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `LegacyFundNameId` int NULL,
    `Name` varchar(256) NOT NULL,
    `ShortName` varchar(256) NULL,
    `Currency` varchar(32) NULL,
    `IsCurrent` tinyint(1) NOT NULL,
    `MonthlyUpload` tinyint(1) NOT NULL,
    `LegacyMainClassId` int NULL,
    `LegacySubClassId` int NULL,
    `LegacyAdministratorId` int NULL,
    `OpenedBy` varchar(256) NULL,
    `UpdatedBy` varchar(256) NULL,
    `LegacyOpenedAt` datetime(6) NULL,
    `LegacyUpdatedAt` datetime(6) NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `InvestmentProductTypeReferences` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `LegacyCompanyProductId` int NULL,
    `Name` varchar(256) NOT NULL,
    `OpenedBy` varchar(256) NULL,
    `UpdatedBy` varchar(256) NULL,
    `LegacyOpenedAt` datetime(6) NULL,
    `LegacyUpdatedAt` datetime(6) NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `KycMainClassReferences` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `LegacyMainClassId` int NULL,
    `Name` varchar(256) NOT NULL,
    `AfrikaansDescription` varchar(512) NULL,
    `EnglishDescription` varchar(512) NULL,
    `OpenedBy` varchar(256) NULL,
    `UpdatedBy` varchar(256) NULL,
    `LegacyOpenedAt` datetime(6) NULL,
    `LegacyUpdatedAt` datetime(6) NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `MarketReferenceValues` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `LegacyMiscInfoId` int NULL,
    `PriceDate` date NULL,
    `Name` varchar(256) NOT NULL,
    `Value` decimal(18,4) NULL,
    `OpenedBy` varchar(256) NULL,
    `UpdatedBy` varchar(256) NULL,
    `LegacyOpenedAt` datetime(6) NULL,
    `LegacyUpdatedAt` datetime(6) NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `KycSubClassReferences` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `LegacySubClassId` int NULL,
    `KycMainClassReferenceId` int NOT NULL,
    `LegacyMainClassId` int NULL,
    `Name` varchar(256) NOT NULL,
    `OpenedBy` varchar(256) NULL,
    `UpdatedBy` varchar(256) NULL,
    `LegacyOpenedAt` datetime(6) NULL,
    `LegacyUpdatedAt` datetime(6) NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_KycSubClassReferences_KycMainClassReferences_KycMainClassRef~` FOREIGN KEY (`KycMainClassReferenceId`) REFERENCES `KycMainClassReferences` (`Id`) ON DELETE CASCADE
);

CREATE INDEX `IX_InvestmentAdministratorReferences_IsCurrent_Name` ON `InvestmentAdministratorReferences` (`IsCurrent`, `Name`);

CREATE UNIQUE INDEX `IX_InvestmentAdministratorReferences_LegacyLispId` ON `InvestmentAdministratorReferences` (`LegacyLispId`);

CREATE INDEX `IX_InvestmentAdministratorReferences_Name` ON `InvestmentAdministratorReferences` (`Name`);

CREATE INDEX `IX_InvestmentFundReferences_IsCurrent_Name` ON `InvestmentFundReferences` (`IsCurrent`, `Name`);

CREATE INDEX `IX_InvestmentFundReferences_LegacyAdministratorId_LegacyMainCla~` ON `InvestmentFundReferences` (`LegacyAdministratorId`, `LegacyMainClassId`, `LegacySubClassId`);

CREATE UNIQUE INDEX `IX_InvestmentFundReferences_LegacyFundNameId` ON `InvestmentFundReferences` (`LegacyFundNameId`);

CREATE INDEX `IX_InvestmentFundReferences_Name` ON `InvestmentFundReferences` (`Name`);

CREATE INDEX `IX_InvestmentFundReferences_ShortName` ON `InvestmentFundReferences` (`ShortName`);

CREATE UNIQUE INDEX `IX_InvestmentProductTypeReferences_LegacyCompanyProductId` ON `InvestmentProductTypeReferences` (`LegacyCompanyProductId`);

CREATE INDEX `IX_InvestmentProductTypeReferences_Name` ON `InvestmentProductTypeReferences` (`Name`);

CREATE UNIQUE INDEX `IX_KycMainClassReferences_LegacyMainClassId` ON `KycMainClassReferences` (`LegacyMainClassId`);

CREATE INDEX `IX_KycMainClassReferences_Name` ON `KycMainClassReferences` (`Name`);

CREATE INDEX `IX_KycSubClassReferences_KycMainClassReferenceId_Name` ON `KycSubClassReferences` (`KycMainClassReferenceId`, `Name`);

CREATE INDEX `IX_KycSubClassReferences_LegacyMainClassId` ON `KycSubClassReferences` (`LegacyMainClassId`);

CREATE UNIQUE INDEX `IX_KycSubClassReferences_LegacySubClassId` ON `KycSubClassReferences` (`LegacySubClassId`);

CREATE UNIQUE INDEX `IX_MarketReferenceValues_LegacyMiscInfoId` ON `MarketReferenceValues` (`LegacyMiscInfoId`);

CREATE INDEX `IX_MarketReferenceValues_Name` ON `MarketReferenceValues` (`Name`);

CREATE INDEX `IX_MarketReferenceValues_PriceDate` ON `MarketReferenceValues` (`PriceDate`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260607203500_AddReferenceDataClosure', '10.0.8');

COMMIT;

