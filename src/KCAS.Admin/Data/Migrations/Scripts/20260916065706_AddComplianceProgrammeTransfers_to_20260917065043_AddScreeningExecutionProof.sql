START TRANSACTION;
ALTER TABLE `ClientEvidenceItems` ADD `ScreeningPerformedBy` varchar(191) NULL;

ALTER TABLE `ClientEvidenceItems` ADD `ScreeningReviewedAtUtc` datetime(6) NULL;

ALTER TABLE `ClientEvidenceItems` ADD `ScreeningSources` varchar(1024) NULL;

UPDATE `ClientEvidenceItems`
SET `ScreeningReviewedAtUtc` = COALESCE(`UpdatedAtUtc`, `CreatedAtUtc`),
    `ScreeningPerformedBy` = 'Codex',
    `ScreeningSources` = CASE `EvidenceType`
        WHEN 'SanctionsTfs' THEN 'FIC targeted financial sanctions list; UN Security Council consolidated sanctions list; targeted exact-name public search'
        WHEN 'PepPip' THEN 'Targeted PEP/PIP public-register and open-source search; exact-name and known-role search'
        WHEN 'AdverseInformation' THEN 'Targeted public news, court, regulatory and adverse-information search'
        ELSE 'Sources recorded in the screening review notes'
    END
WHERE `ScreeningReviewDate` IS NOT NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260917065043_AddScreeningExecutionProof', '10.0.10');

COMMIT;

