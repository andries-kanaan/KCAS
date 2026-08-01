START TRANSACTION;
CREATE TABLE `ClientInvestmentReconciliationReviews` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientId` int NOT NULL,
    `ClientInvestmentAccountId` int NOT NULL,
    `Outcome` varchar(32) NOT NULL,
    `RelatedClientInvestmentAccountId` int NULL,
    `AppliedSurrenderDate` date NULL,
    `EvidenceReference` varchar(512) NOT NULL,
    `Reason` varchar(1000) NOT NULL,
    `SnapshotSha256` varchar(64) NOT NULL,
    `ReviewedAtUtc` datetime(6) NOT NULL,
    `ReviewedBy` varchar(191) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientInvestmentReconciliationReviews_ClientInvestmentAccoun~` FOREIGN KEY (`ClientInvestmentAccountId`) REFERENCES `ClientInvestmentAccounts` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_ClientInvestmentReconciliationReviews_ClientInvestmentAccou~1` FOREIGN KEY (`RelatedClientInvestmentAccountId`) REFERENCES `ClientInvestmentAccounts` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_ClientInvestmentReconciliationReviews_Clients_ClientId` FOREIGN KEY (`ClientId`) REFERENCES `Clients` (`Id`) ON DELETE CASCADE
);

CREATE INDEX `IX_ClientInvestmentReconciliationReviews_ClientId_ClientInvestm~` ON `ClientInvestmentReconciliationReviews` (`ClientId`, `ClientInvestmentAccountId`, `ReviewedAtUtc`);

CREATE INDEX `IX_ClientInvestmentReconciliationReviews_ClientInvestmentAccoun~` ON `ClientInvestmentReconciliationReviews` (`ClientInvestmentAccountId`);

CREATE INDEX `IX_ClientInvestmentReconciliationReviews_Outcome` ON `ClientInvestmentReconciliationReviews` (`Outcome`);

CREATE INDEX `IX_ClientInvestmentReconciliationReviews_RelatedClientInvestmen~` ON `ClientInvestmentReconciliationReviews` (`RelatedClientInvestmentAccountId`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260801154706_AddClientInvestmentReconciliationReviews', '10.0.10');

COMMIT;

