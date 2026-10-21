START TRANSACTION;
ALTER TABLE `PerformanceFeeProfiles` ADD `StatementReviewJson` longtext NULL;

UPDATE `PerformanceFeeProfiles` SET `StatementReviewJson` = '{}' WHERE `StatementReviewJson` IS NULL;

ALTER TABLE `PerformanceFeeProfiles` MODIFY `StatementReviewJson` longtext NOT NULL;

CREATE TABLE `PerformanceFeeStatements` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `PerformanceFeeProfileId` int NOT NULL,
    `FileName` varchar(256) NOT NULL,
    `Sha256` varchar(64) NOT NULL,
    `Content` longblob NOT NULL,
    `DocumentJson` longtext NOT NULL,
    `ImportedBy` varchar(191) NOT NULL,
    `ImportedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_PerformanceFeeStatements_PerformanceFeeProfiles_PerformanceF~` FOREIGN KEY (`PerformanceFeeProfileId`) REFERENCES `PerformanceFeeProfiles` (`Id`) ON DELETE CASCADE
);

CREATE UNIQUE INDEX `IX_PerformanceFeeStatements_PerformanceFeeProfileId_Sha256` ON `PerformanceFeeStatements` (`PerformanceFeeProfileId`, `Sha256`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261020212355_AddPerformanceFeeStatements', '10.0.10');

COMMIT;
