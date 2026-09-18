START TRANSACTION;
ALTER TABLE `ClientAdviceCases` ADD `RiskMethodologyCode` varchar(64) NOT NULL DEFAULT 'KCAS_EVIDENCED_V1';

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260918201716_AddSelectableAdviceRiskMethodology', '10.0.10');

COMMIT;
