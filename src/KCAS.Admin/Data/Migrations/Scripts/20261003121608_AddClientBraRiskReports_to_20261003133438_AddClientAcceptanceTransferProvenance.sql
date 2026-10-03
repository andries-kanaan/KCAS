START TRANSACTION;
ALTER TABLE `ClientOnboardingProfiles` ADD `ImportSourceReference` longtext NULL;

ALTER TABLE `ClientCodexReviewRequests` ADD `ImportSourceReference` longtext NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261003133438_AddClientAcceptanceTransferProvenance', '10.0.10');

COMMIT;
