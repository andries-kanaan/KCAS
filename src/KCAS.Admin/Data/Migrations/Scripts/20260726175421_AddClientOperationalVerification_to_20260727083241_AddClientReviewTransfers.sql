START TRANSACTION;
CREATE TABLE `ClientReviewTransferRecords` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `PackageId` varchar(36) NOT NULL,
    `Direction` varchar(16) NOT NULL,
    `ContentSha256` varchar(64) NOT NULL,
    `ClientId` int NOT NULL,
    `Status` varchar(32) NOT NULL,
    `FileName` varchar(260) NOT NULL,
    `StoragePath` varchar(512) NOT NULL,
    `SummaryJson` longtext NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `AppliedAtUtc` datetime(6) NULL,
    `AppliedBy` varchar(191) NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientReviewTransferRecords_Clients_ClientId` FOREIGN KEY (`ClientId`) REFERENCES `Clients` (`Id`) ON DELETE RESTRICT
);

CREATE INDEX `IX_ClientReviewTransferRecords_ClientId_CreatedAtUtc` ON `ClientReviewTransferRecords` (`ClientId`, `CreatedAtUtc`);

CREATE INDEX `IX_ClientReviewTransferRecords_Direction_ContentSha256` ON `ClientReviewTransferRecords` (`Direction`, `ContentSha256`);

CREATE UNIQUE INDEX `IX_ClientReviewTransferRecords_Direction_PackageId` ON `ClientReviewTransferRecords` (`Direction`, `PackageId`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260727083241_AddClientReviewTransfers', '10.0.10');

COMMIT;
