START TRANSACTION;
CREATE TABLE `ClientEvidenceInvestmentLinks` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientEvidenceItemId` int NOT NULL,
    `ClientInvestmentAccountId` int NOT NULL,
    `LinkedAtUtc` datetime(6) NOT NULL,
    `LinkedBy` varchar(191) NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientEvidenceInvestmentLinks_ClientEvidenceItems_ClientEvid~` FOREIGN KEY (`ClientEvidenceItemId`) REFERENCES `ClientEvidenceItems` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_ClientEvidenceInvestmentLinks_ClientInvestmentAccounts_Clien~` FOREIGN KEY (`ClientInvestmentAccountId`) REFERENCES `ClientInvestmentAccounts` (`Id`) ON DELETE CASCADE
);

CREATE UNIQUE INDEX `IX_ClientEvidenceInvestmentLinks_ClientEvidenceItemId_ClientInv~` ON `ClientEvidenceInvestmentLinks` (`ClientEvidenceItemId`, `ClientInvestmentAccountId`);

CREATE INDEX `IX_ClientEvidenceInvestmentLinks_ClientInvestmentAccountId` ON `ClientEvidenceInvestmentLinks` (`ClientInvestmentAccountId`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260917190109_AddSourceOfFundsInvestmentCoverage', '10.0.10');

COMMIT;
