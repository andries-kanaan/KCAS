START TRANSACTION;
CREATE TABLE `GoAmlReminderDeliveries` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ReminderDate` date NOT NULL,
    `Recipient` varchar(191) NOT NULL,
    `Status` varchar(32) NOT NULL,
    `Attempts` int NOT NULL,
    `LastAttemptAtUtc` datetime(6) NULL,
    `SentAtUtc` datetime(6) NULL,
    `Error` varchar(512) NULL,
    PRIMARY KEY (`Id`)
);

CREATE UNIQUE INDEX `IX_GoAmlReminderDeliveries_ReminderDate` ON `GoAmlReminderDeliveries` (`ReminderDate`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261105095143_AddGoAmlReminderDeliveries', '10.0.10');

COMMIT;

