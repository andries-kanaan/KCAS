START TRANSACTION;
ALTER TABLE `ClientAdviceCases` ADD `TransferKey` varchar(36) NULL;

CREATE TABLE `ClientAdviceTransferRecords` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `PackageId` varchar(36) NOT NULL,
    `Direction` varchar(16) NOT NULL,
    `ContentSha256` varchar(64) NOT NULL,
    `ClientId` int NOT NULL,
    `Status` varchar(32) NOT NULL,
    `FileName` varchar(260) NOT NULL,
    `StoragePath` varchar(512) NOT NULL,
    `CaseCount` int NOT NULL,
    `DocumentCount` int NOT NULL,
    `SummaryJson` longtext NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `AppliedAtUtc` datetime(6) NULL,
    `AppliedBy` varchar(191) NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientAdviceTransferRecords_Clients_ClientId` FOREIGN KEY (`ClientId`) REFERENCES `Clients` (`Id`) ON DELETE RESTRICT
);

CREATE UNIQUE INDEX `IX_ClientAdviceCases_TransferKey` ON `ClientAdviceCases` (`TransferKey`);

CREATE INDEX `IX_ClientAdviceTransferRecords_ClientId_CreatedAtUtc` ON `ClientAdviceTransferRecords` (`ClientId`, `CreatedAtUtc`);

CREATE INDEX `IX_ClientAdviceTransferRecords_Direction_ContentSha256` ON `ClientAdviceTransferRecords` (`Direction`, `ContentSha256`);

CREATE UNIQUE INDEX `IX_ClientAdviceTransferRecords_Direction_PackageId` ON `ClientAdviceTransferRecords` (`Direction`, `PackageId`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260918211155_AddClientAdviceTransfers', '10.0.10');

COMMIT;
