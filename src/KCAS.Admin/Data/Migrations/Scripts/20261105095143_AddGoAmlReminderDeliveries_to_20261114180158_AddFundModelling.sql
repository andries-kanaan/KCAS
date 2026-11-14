START TRANSACTION;
CREATE TABLE `ModelEvents` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `FundId` int NULL,
    `Action` varchar(80) NOT NULL,
    `Actor` varchar(256) NOT NULL,
    `AtUtc` datetime(6) NOT NULL,
    `DetailJson` longtext NOT NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `ModelFunds` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Key` varchar(64) NOT NULL,
    `Name` varchar(160) NOT NULL,
    `Currency` varchar(3) NOT NULL,
    `Archived` tinyint(1) NOT NULL,
    `CashValue` double NOT NULL,
    `HoldingsJson` longtext NOT NULL,
    `ScenariosJson` longtext NOT NULL,
    `Revision` varchar(64) NOT NULL,
    `UpdatedUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `ModelSecurities` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Key` varchar(100) NOT NULL,
    `Name` varchar(200) NOT NULL,
    `Currency` varchar(3) NOT NULL,
    `ProviderSymbol` varchar(50) NOT NULL,
    `SeriesJson` longtext NOT NULL,
    `UpdatedUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `ModelRuns` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `Key` varchar(64) NOT NULL,
    `FundId` int NOT NULL,
    `Fingerprint` varchar(64) NOT NULL,
    `State` varchar(30) NOT NULL,
    `Progress` varchar(400) NOT NULL,
    `InputJson` longtext NOT NULL,
    `ResultJson` longtext NOT NULL,
    `Actor` varchar(256) NOT NULL,
    `CreatedUtc` datetime(6) NOT NULL,
    `FinishedUtc` datetime(6) NULL,
    `CancelRequested` tinyint(1) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ModelRuns_ModelFunds_FundId` FOREIGN KEY (`FundId`) REFERENCES `ModelFunds` (`Id`) ON DELETE RESTRICT
);

CREATE UNIQUE INDEX `IX_ModelFunds_Key` ON `ModelFunds` (`Key`);

CREATE INDEX `IX_ModelRuns_FundId_Fingerprint` ON `ModelRuns` (`FundId`, `Fingerprint`);

CREATE UNIQUE INDEX `IX_ModelRuns_Key` ON `ModelRuns` (`Key`);

CREATE UNIQUE INDEX `IX_ModelSecurities_Key` ON `ModelSecurities` (`Key`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261114180158_AddFundModelling', '10.0.10');

COMMIT;

