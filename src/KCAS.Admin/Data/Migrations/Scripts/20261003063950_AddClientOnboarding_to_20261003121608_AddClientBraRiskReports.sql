START TRANSACTION;
CREATE TABLE `ClientBraRiskReports` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientId` int NOT NULL,
    `SourceContentHash` varchar(64) NOT NULL,
    `MethodVersion` varchar(64) NOT NULL,
    `BraReference` longtext NOT NULL,
    `ContentJson` longtext NOT NULL,
    `PerformedBy` varchar(191) NOT NULL,
    `RecordedBy` varchar(191) NOT NULL,
    `RecordedAtUtc` datetime(6) NOT NULL,
    `ImportPackageId` varchar(64) NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientBraRiskReports_Clients_ClientId` FOREIGN KEY (`ClientId`) REFERENCES `Clients` (`Id`) ON DELETE RESTRICT
);

CREATE INDEX `IX_ClientBraRiskReports_ClientId_RecordedAtUtc` ON `ClientBraRiskReports` (`ClientId`, `RecordedAtUtc`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261003121608_AddClientBraRiskReports', '10.0.10');

COMMIT;
