START TRANSACTION;

UPDATE `ClientEvidenceRequirements`
SET `RequiresExpiryDate` = FALSE
WHERE `EvidenceType` IN ('Identity', 'Address');

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260727103500_RemoveArtificialIdentityAddressExpiry', '10.0.10');

COMMIT;
