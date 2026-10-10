START TRANSACTION;
CREATE TABLE `InvestmentFundingConnections` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `SourceAccountId` int NOT NULL,
    `DestinationAccountId` int NOT NULL,
    `MatchedThroughDate` date NOT NULL,
    `SourceSnapshot` varchar(64) NOT NULL,
    `DestinationSnapshot` varchar(64) NOT NULL,
    `EvidenceReference` varchar(512) NOT NULL,
    `Reason` varchar(1000) NOT NULL,
    `PerformedBy` varchar(191) NOT NULL,
    `RecordedBy` varchar(191) NOT NULL,
    `RecordedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_InvestmentFundingConnections_ClientInvestmentAccounts_Destin~` FOREIGN KEY (`DestinationAccountId`) REFERENCES `ClientInvestmentAccounts` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_InvestmentFundingConnections_ClientInvestmentAccounts_Source~` FOREIGN KEY (`SourceAccountId`) REFERENCES `ClientInvestmentAccounts` (`Id`) ON DELETE RESTRICT
);

CREATE INDEX `IX_InvestmentFundingConnections_DestinationAccountId` ON `InvestmentFundingConnections` (`DestinationAccountId`);

CREATE INDEX `IX_InvestmentFundingConnections_SourceAccountId_RecordedAtUtc` ON `InvestmentFundingConnections` (`SourceAccountId`, `RecordedAtUtc`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261007193401_AddInvestmentFundingConnections', '10.0.10');

COMMIT;
