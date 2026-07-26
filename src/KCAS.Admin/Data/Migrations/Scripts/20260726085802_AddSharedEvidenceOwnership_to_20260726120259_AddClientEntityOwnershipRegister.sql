START TRANSACTION;
ALTER TABLE `ClientEvidenceItems` ADD `ClientRelatedPartyId` int NULL;

CREATE TABLE `ClientEntityProfiles` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientId` int NOT NULL,
    `LegalForm` varchar(64) NULL,
    `RegistrationNumber` varchar(128) NULL,
    `RegistrationCountry` varchar(96) NULL,
    `EstablishmentDate` date NULL,
    `NatureOfBusinessOrPurpose` varchar(500) NULL,
    `OwnershipReviewStatus` varchar(32) NOT NULL,
    `ControlConclusion` varchar(32) NULL,
    `ControlConclusionReason` varchar(1000) NULL,
    `OwnershipReviewedAtUtc` datetime(6) NULL,
    `OwnershipReviewedBy` varchar(191) NULL,
    `NextOwnershipReviewDate` date NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NULL,
    `UpdatedBy` varchar(191) NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientEntityProfiles_Clients_ClientId` FOREIGN KEY (`ClientId`) REFERENCES `Clients` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `ClientRelatedParties` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientId` int NOT NULL,
    `PartyType` varchar(32) NOT NULL,
    `DisplayName` varchar(240) NOT NULL,
    `SouthAfricanIdNumber` varchar(13) NULL,
    `PassportNumber` varchar(64) NULL,
    `PassportCountry` varchar(96) NULL,
    `RegistrationNumber` varchar(128) NULL,
    `BirthDate` date NULL,
    `Nationality` varchar(96) NULL,
    `CountryOfResidence` varchar(96) NULL,
    `OwnershipPercent` decimal(5,2) NULL,
    `ControlBasis` varchar(1000) NULL,
    `AuthorityBasis` varchar(1000) NULL,
    `EffectiveFrom` date NULL,
    `EffectiveTo` date NULL,
    `IsActive` tinyint(1) NOT NULL,
    `Notes` varchar(1000) NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NULL,
    `UpdatedBy` varchar(191) NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientRelatedParties_Clients_ClientId` FOREIGN KEY (`ClientId`) REFERENCES `Clients` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `ClientRelatedPartyEvidenceLinks` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientRelatedPartyId` int NOT NULL,
    `ClientEvidenceItemId` int NOT NULL,
    `Purpose` varchar(32) NOT NULL,
    `LinkedAtUtc` datetime(6) NOT NULL,
    `LinkedBy` varchar(191) NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientRelatedPartyEvidenceLinks_ClientEvidenceItems_ClientEv~` FOREIGN KEY (`ClientEvidenceItemId`) REFERENCES `ClientEvidenceItems` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_ClientRelatedPartyEvidenceLinks_ClientRelatedParties_ClientR~` FOREIGN KEY (`ClientRelatedPartyId`) REFERENCES `ClientRelatedParties` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `ClientRelatedPartyRoles` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientRelatedPartyId` int NOT NULL,
    `RoleCode` varchar(64) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientRelatedPartyRoles_ClientRelatedParties_ClientRelatedPa~` FOREIGN KEY (`ClientRelatedPartyId`) REFERENCES `ClientRelatedParties` (`Id`) ON DELETE CASCADE
);

CREATE INDEX `IX_ClientEvidenceItems_ClientRelatedPartyId` ON `ClientEvidenceItems` (`ClientRelatedPartyId`);

CREATE UNIQUE INDEX `IX_ClientEntityProfiles_ClientId` ON `ClientEntityProfiles` (`ClientId`);

CREATE INDEX `IX_ClientEntityProfiles_OwnershipReviewStatus_NextOwnershipRevi~` ON `ClientEntityProfiles` (`OwnershipReviewStatus`, `NextOwnershipReviewDate`);

CREATE INDEX `IX_ClientRelatedParties_ClientId_IsActive` ON `ClientRelatedParties` (`ClientId`, `IsActive`);

CREATE INDEX `IX_ClientRelatedParties_DisplayName` ON `ClientRelatedParties` (`DisplayName`);

CREATE INDEX `IX_ClientRelatedPartyEvidenceLinks_ClientEvidenceItemId_Purpose` ON `ClientRelatedPartyEvidenceLinks` (`ClientEvidenceItemId`, `Purpose`);

CREATE UNIQUE INDEX `IX_ClientRelatedPartyEvidenceLinks_ClientRelatedPartyId_ClientE~` ON `ClientRelatedPartyEvidenceLinks` (`ClientRelatedPartyId`, `ClientEvidenceItemId`, `Purpose`);

CREATE UNIQUE INDEX `IX_ClientRelatedPartyRoles_ClientRelatedPartyId_RoleCode` ON `ClientRelatedPartyRoles` (`ClientRelatedPartyId`, `RoleCode`);

CREATE INDEX `IX_ClientRelatedPartyRoles_RoleCode` ON `ClientRelatedPartyRoles` (`RoleCode`);

ALTER TABLE `ClientEvidenceItems` ADD CONSTRAINT `FK_ClientEvidenceItems_ClientRelatedParties_ClientRelatedPartyId` FOREIGN KEY (`ClientRelatedPartyId`) REFERENCES `ClientRelatedParties` (`Id`) ON DELETE RESTRICT;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260726120259_AddClientEntityOwnershipRegister', '10.0.10');

COMMIT;
