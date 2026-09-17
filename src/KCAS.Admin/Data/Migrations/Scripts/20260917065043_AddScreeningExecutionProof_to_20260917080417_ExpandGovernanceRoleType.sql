START TRANSACTION;
ALTER TABLE `GovernanceRoleAssignments` MODIFY `RoleType` varchar(191) NOT NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260917080417_ExpandGovernanceRoleType', '10.0.10');

COMMIT;

