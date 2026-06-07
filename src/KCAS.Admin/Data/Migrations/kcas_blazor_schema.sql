CREATE TABLE IF NOT EXISTS `__EFMigrationsHistory` (
    `MigrationId` varchar(150) NOT NULL,
    `ProductVersion` varchar(32) NOT NULL,
    PRIMARY KEY (`MigrationId`)
);

START TRANSACTION;
CREATE TABLE `AspNetRoles` (
    `Id` varchar(64) NOT NULL,
    `Name` varchar(191) NULL,
    `NormalizedName` varchar(191) NULL,
    `ConcurrencyStamp` longtext NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `AspNetUsers` (
    `Id` varchar(64) NOT NULL,
    `UserName` varchar(191) NULL,
    `NormalizedUserName` varchar(191) NULL,
    `Email` varchar(191) NULL,
    `NormalizedEmail` varchar(191) NULL,
    `EmailConfirmed` tinyint(1) NOT NULL,
    `PasswordHash` longtext NULL,
    `SecurityStamp` longtext NULL,
    `ConcurrencyStamp` longtext NULL,
    `PhoneNumber` varchar(256) NULL,
    `PhoneNumberConfirmed` tinyint(1) NOT NULL,
    `TwoFactorEnabled` tinyint(1) NOT NULL,
    `LockoutEnd` datetime NULL,
    `LockoutEnabled` tinyint(1) NOT NULL,
    `AccessFailedCount` int NOT NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `Clients` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientCode` varchar(30) NOT NULL,
    `FirstName` varchar(100) NOT NULL,
    `LastName` varchar(100) NOT NULL,
    `SouthAfricanIdNumber` varchar(13) NULL,
    `Email` varchar(254) NULL,
    `MobileNumber` varchar(30) NULL,
    `IsActive` tinyint(1) NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `AspNetRoleClaims` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `RoleId` varchar(64) NOT NULL,
    `ClaimType` longtext NULL,
    `ClaimValue` longtext NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_AspNetRoleClaims_AspNetRoles_RoleId` FOREIGN KEY (`RoleId`) REFERENCES `AspNetRoles` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `AspNetUserClaims` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `UserId` varchar(64) NOT NULL,
    `ClaimType` longtext NULL,
    `ClaimValue` longtext NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_AspNetUserClaims_AspNetUsers_UserId` FOREIGN KEY (`UserId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `AspNetUserLogins` (
    `LoginProvider` varchar(64) NOT NULL,
    `ProviderKey` varchar(64) NOT NULL,
    `ProviderDisplayName` longtext NULL,
    `UserId` varchar(64) NOT NULL,
    PRIMARY KEY (`LoginProvider`, `ProviderKey`),
    CONSTRAINT `FK_AspNetUserLogins_AspNetUsers_UserId` FOREIGN KEY (`UserId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `AspNetUserRoles` (
    `UserId` varchar(64) NOT NULL,
    `RoleId` varchar(64) NOT NULL,
    PRIMARY KEY (`UserId`, `RoleId`),
    CONSTRAINT `FK_AspNetUserRoles_AspNetRoles_RoleId` FOREIGN KEY (`RoleId`) REFERENCES `AspNetRoles` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_AspNetUserRoles_AspNetUsers_UserId` FOREIGN KEY (`UserId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `AspNetUserTokens` (
    `UserId` varchar(64) NOT NULL,
    `LoginProvider` varchar(64) NOT NULL,
    `Name` varchar(64) NOT NULL,
    `Value` longtext NULL,
    PRIMARY KEY (`UserId`, `LoginProvider`, `Name`),
    CONSTRAINT `FK_AspNetUserTokens_AspNetUsers_UserId` FOREIGN KEY (`UserId`) REFERENCES `AspNetUsers` (`Id`) ON DELETE CASCADE
);

CREATE INDEX `IX_AspNetRoleClaims_RoleId` ON `AspNetRoleClaims` (`RoleId`);

CREATE UNIQUE INDEX `RoleNameIndex` ON `AspNetRoles` (`NormalizedName`);

CREATE INDEX `IX_AspNetUserClaims_UserId` ON `AspNetUserClaims` (`UserId`);

CREATE INDEX `IX_AspNetUserLogins_UserId` ON `AspNetUserLogins` (`UserId`);

CREATE INDEX `IX_AspNetUserRoles_RoleId` ON `AspNetUserRoles` (`RoleId`);

CREATE INDEX `EmailIndex` ON `AspNetUsers` (`NormalizedEmail`);

CREATE UNIQUE INDEX `UserNameIndex` ON `AspNetUsers` (`NormalizedUserName`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260529223052_InitialKcasSchema', '10.0.8');

ALTER TABLE `AspNetUsers` ADD `ApprovedAtUtc` datetime(6) NULL;

ALTER TABLE `AspNetUsers` ADD `ApprovedByUserId` varchar(64) NULL;

ALTER TABLE `AspNetUsers` ADD `CreatedAtUtc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6);

ALTER TABLE `AspNetUsers` ADD `DisplayName` varchar(191) NULL;

ALTER TABLE `AspNetUsers` ADD `IsApproved` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `AspNetUsers` ADD `WindowsAccountName` varchar(191) NULL;

CREATE INDEX `IX_AspNetUsers_WindowsAccountName` ON `AspNetUsers` (`WindowsAccountName`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260531080449_AddSecurityRbac', '10.0.8');

ALTER TABLE `Clients` DROP COLUMN `ClientCode`;

ALTER TABLE `Clients` DROP COLUMN `Email`;

ALTER TABLE `Clients` DROP COLUMN `FirstName`;

ALTER TABLE `Clients` DROP COLUMN `LastName`;

ALTER TABLE `Clients` DROP COLUMN `SouthAfricanIdNumber`;

ALTER TABLE `Clients` DROP COLUMN `MobileNumber`;

ALTER TABLE `Clients` MODIFY `CreatedAtUtc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6);

ALTER TABLE `Clients` ADD `ClientFolder` varchar(512) NULL;

ALTER TABLE `Clients` ADD `Title` varchar(30) NULL;

ALTER TABLE `Clients` ADD `DisplayName` varchar(220) NOT NULL DEFAULT '';

ALTER TABLE `Clients` ADD `FullName` varchar(200) NULL;

ALTER TABLE `Clients` ADD `Initials` varchar(50) NULL;

ALTER TABLE `Clients` ADD `KanaanId` varchar(30) NULL;

ALTER TABLE `Clients` ADD `Language` varchar(50) NULL;

ALTER TABLE `Clients` ADD `LegacyClientId` int NULL;

ALTER TABLE `Clients` ADD `SurnameOrEntityName` varchar(200) NOT NULL DEFAULT '';

CREATE TABLE `ClientAddresses` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientId` int NOT NULL,
    `AddressType` varchar(40) NOT NULL,
    `LinesRaw` varchar(1000) NOT NULL,
    `SortOrder` int NOT NULL,
    `LegacySourceField` varchar(80) NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientAddresses_Clients_ClientId` FOREIGN KEY (`ClientId`) REFERENCES `Clients` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `ClientContactPoints` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientId` int NOT NULL,
    `ContactType` varchar(30) NOT NULL,
    `Label` varchar(80) NULL,
    `Value` varchar(254) NOT NULL,
    `IsPrimary` tinyint(1) NOT NULL,
    `SortOrder` int NOT NULL,
    `LegacySourceField` varchar(80) NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientContactPoints_Clients_ClientId` FOREIGN KEY (`ClientId`) REFERENCES `Clients` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `ClientFinancialProfiles` (
    `ClientId` int NOT NULL,
    `Employer` varchar(150) NULL,
    `Occupation` varchar(150) NULL,
    `GrossMonthlySalary` decimal(18,2) NULL,
    `GrossAnnualSalary` decimal(18,2) NULL,
    `MonthlyExpenses` decimal(18,2) NULL,
    `YearlyBonus` decimal(18,2) NULL,
    `OtherIncome` decimal(18,2) NULL,
    `RetirementAge` int NULL,
    `PensionFundName` varchar(150) NULL,
    `EmployerPensionContributionAmount` decimal(18,2) NULL,
    `EmployerPensionContributionPercent` decimal(9,4) NULL,
    `CapitalRequirementPercent` decimal(9,4) NULL,
    `MinimumRetirementIncomePercent` decimal(9,4) NULL,
    `ExpectedRetirementIncomePercent` decimal(9,4) NULL,
    `BankDetailRaw` varchar(1000) NULL,
    `WillDetailRaw` varchar(1000) NULL,
    `OtherGoalsRaw` varchar(1000) NULL,
    `OtherDetailsRaw` varchar(1000) NULL,
    PRIMARY KEY (`ClientId`),
    CONSTRAINT `FK_ClientFinancialProfiles_Clients_ClientId` FOREIGN KEY (`ClientId`) REFERENCES `Clients` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `ClientLegacySnapshots` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientId` int NOT NULL,
    `SourceTable` varchar(80) NOT NULL,
    `SourceId` int NOT NULL,
    `PayloadJson` longtext NOT NULL,
    `ImportedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientLegacySnapshots_Clients_ClientId` FOREIGN KEY (`ClientId`) REFERENCES `Clients` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `ClientPersonalProfiles` (
    `ClientId` int NOT NULL,
    `SouthAfricanIdNumber` varchar(13) NULL,
    `Gender` varchar(20) NULL,
    `MaritalStatus` varchar(100) NULL,
    `TaxOffice` varchar(100) NULL,
    `TaxNumber` varchar(50) NULL,
    `IsTaxClient` tinyint(1) NULL,
    `HighestQualification` varchar(150) NULL,
    `Smoker` tinyint(1) NULL,
    `NumberOfDependents` int NULL,
    PRIMARY KEY (`ClientId`),
    CONSTRAINT `FK_ClientPersonalProfiles_Clients_ClientId` FOREIGN KEY (`ClientId`) REFERENCES `Clients` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `ClientRelationships` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientId` int NOT NULL,
    `RelationshipType` varchar(40) NOT NULL,
    `LegacyRelatedClientId` int NULL,
    `Name` varchar(200) NULL,
    `Initials` varchar(50) NULL,
    `Gender` varchar(20) NULL,
    `BirthDate` datetime(6) NULL,
    `SouthAfricanIdNumber` varchar(13) NULL,
    `Email` varchar(254) NULL,
    `HomePhone` varchar(30) NULL,
    `WorkPhone` varchar(30) NULL,
    `MobilePhone` varchar(30) NULL,
    `Employer` varchar(150) NULL,
    `Occupation` varchar(150) NULL,
    `HighestQualification` varchar(150) NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientRelationships_Clients_ClientId` FOREIGN KEY (`ClientId`) REFERENCES `Clients` (`Id`) ON DELETE CASCADE
);

CREATE INDEX `IX_Clients_DisplayName` ON `Clients` (`DisplayName`);

CREATE INDEX `IX_Clients_KanaanId` ON `Clients` (`KanaanId`);

CREATE UNIQUE INDEX `IX_Clients_LegacyClientId` ON `Clients` (`LegacyClientId`);

CREATE INDEX `IX_ClientAddresses_ClientId_AddressType` ON `ClientAddresses` (`ClientId`, `AddressType`);

CREATE INDEX `IX_ClientContactPoints_ClientId_ContactType_IsPrimary` ON `ClientContactPoints` (`ClientId`, `ContactType`, `IsPrimary`);

CREATE INDEX `IX_ClientContactPoints_Value` ON `ClientContactPoints` (`Value`);

CREATE INDEX `IX_ClientLegacySnapshots_ClientId` ON `ClientLegacySnapshots` (`ClientId`);

CREATE INDEX `IX_ClientLegacySnapshots_SourceTable_SourceId` ON `ClientLegacySnapshots` (`SourceTable`, `SourceId`);

CREATE INDEX `IX_ClientPersonalProfiles_SouthAfricanIdNumber` ON `ClientPersonalProfiles` (`SouthAfricanIdNumber`);

CREATE INDEX `IX_ClientRelationships_ClientId_RelationshipType` ON `ClientRelationships` (`ClientId`, `RelationshipType`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260531130752_AddNormalizedClientImport', '10.0.8');

ALTER TABLE `ClientRelationships` ADD `EmployerPensionContributionAmount` decimal(18,2) NULL;

ALTER TABLE `ClientRelationships` ADD `EmployerPensionContributionPercent` decimal(9,4) NULL;

ALTER TABLE `ClientRelationships` ADD `GrossAnnualSalary` decimal(18,2) NULL;

ALTER TABLE `ClientRelationships` ADD `GrossMonthlySalary` decimal(18,2) NULL;

ALTER TABLE `ClientRelationships` ADD `OtherIncome` decimal(18,2) NULL;

ALTER TABLE `ClientRelationships` ADD `PensionFundName` varchar(150) NULL;

ALTER TABLE `ClientRelationships` ADD `YearlyBonus` decimal(18,2) NULL;

ALTER TABLE `ClientPersonalProfiles` ADD `FamilyDetailRaw` varchar(1000) NULL;

ALTER TABLE `ClientPersonalProfiles` ADD `WorkdayTravelPercent` decimal(9,4) NULL;

ALTER TABLE `ClientFinancialProfiles` ADD `PensionFundTax` decimal(18,2) NULL;

ALTER TABLE `ClientFinancialProfiles` ADD `PreservationFundLumpSumPercent` decimal(9,4) NULL;

ALTER TABLE `ClientFinancialProfiles` ADD `RepresentativeAlternativeInvestmentsPercent` decimal(9,4) NULL;

ALTER TABLE `ClientFinancialProfiles` ADD `RepresentativeEquitiesPercent` decimal(9,4) NULL;

ALTER TABLE `ClientFinancialProfiles` ADD `RepresentativeFixedPropertyPercent` decimal(9,4) NULL;

ALTER TABLE `ClientFinancialProfiles` ADD `RepresentativeName` varchar(150) NULL;

ALTER TABLE `ClientFinancialProfiles` ADD `RepresentativeOffshorePercent` decimal(9,4) NULL;

ALTER TABLE `ClientFinancialProfiles` ADD `RetirementAnnuityTax` decimal(18,2) NULL;

ALTER TABLE `ClientFinancialProfiles` ADD `RetirementProvisionTax` decimal(18,2) NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260531132831_SurfaceLegacyClientSections', '10.0.8');

CREATE TABLE `ClientNotes` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientId` int NOT NULL,
    `LegacyClientNoteId` int NOT NULL,
    `NoteDate` date NULL,
    `Title` varchar(256) NULL,
    `Details` longtext NULL,
    `IsDeleted` tinyint(1) NOT NULL,
    `IsFinal` tinyint(1) NOT NULL,
    `OpenedBy` varchar(256) NULL,
    `UpdatedBy` varchar(256) NULL,
    `LegacyOpenedByUserId` int NULL,
    `LegacyUpdatedByUserId` int NULL,
    `LegacyOpenedAt` datetime(6) NULL,
    `LegacyUpdatedAt` datetime(6) NULL,
    `PayloadJson` longtext NOT NULL,
    `ImportedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientNotes_Clients_ClientId` FOREIGN KEY (`ClientId`) REFERENCES `Clients` (`Id`) ON DELETE CASCADE
);

CREATE INDEX `IX_ClientNotes_ClientId_NoteDate` ON `ClientNotes` (`ClientId`, `NoteDate`);

CREATE UNIQUE INDEX `IX_ClientNotes_LegacyClientNoteId` ON `ClientNotes` (`LegacyClientNoteId`);

CREATE INDEX `IX_ClientNotes_Title` ON `ClientNotes` (`Title`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260531135015_AddClientNotes', '10.0.8');

CREATE TABLE `ClientKycPolicies` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientId` int NOT NULL,
    `LegacyKycId` int NOT NULL,
    `LegacyClientId` int NULL,
    `KanaanId` varchar(256) NULL,
    `LegacyMainClassId` int NULL,
    `MainClassName` varchar(256) NULL,
    `LegacySubClassId` int NULL,
    `SubClassName` varchar(256) NULL,
    `SubClassExtra` varchar(256) NULL,
    `Administrator` varchar(256) NULL,
    `Product` varchar(256) NULL,
    `PolicyNumber` varchar(256) NULL,
    `Description` longtext NULL,
    `Fund` varchar(256) NULL,
    `Value` decimal(18,2) NULL,
    `LifeCover` decimal(18,2) NULL,
    `DisabilityCover` decimal(18,2) NULL,
    `DreadDiseaseCover` decimal(18,2) NULL,
    `CompulsoryContributionValue` decimal(18,2) NULL,
    `VoluntaryContributionValue` decimal(18,2) NULL,
    `Debt` decimal(18,2) NULL,
    `MonthlyPremium` decimal(18,2) NULL,
    `OnceOffPremium` decimal(18,2) NULL,
    `MonthlyIncome` decimal(18,2) NULL,
    `CapitalAdequacyRatioPercent` decimal(9,4) NULL,
    `TaxPercent` decimal(9,4) NULL,
    `IncludeInCalculations` tinyint(1) NOT NULL,
    `SurrenderOrLiquidate` tinyint(1) NOT NULL,
    `IsRetirementAnnuity` tinyint(1) NOT NULL,
    `IsPreservationFund` tinyint(1) NOT NULL,
    `IsRetrenchmentPackage` tinyint(1) NOT NULL,
    `IsQuote` tinyint(1) NOT NULL,
    `ValuationDate` datetime(6) NULL,
    `OpenedBy` varchar(256) NULL,
    `UpdatedBy` varchar(256) NULL,
    `LegacyOpenedByUserId` int NULL,
    `LegacyUpdatedByUserId` int NULL,
    `LegacyOpenedAt` datetime(6) NULL,
    `LegacyUpdatedAt` datetime(6) NULL,
    `PayloadJson` longtext NOT NULL,
    `ImportedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientKycPolicies_Clients_ClientId` FOREIGN KEY (`ClientId`) REFERENCES `Clients` (`Id`) ON DELETE CASCADE
);

CREATE INDEX `IX_ClientKycPolicies_ClientId` ON `ClientKycPolicies` (`ClientId`);

CREATE INDEX `IX_ClientKycPolicies_IncludeInCalculations_IsQuote` ON `ClientKycPolicies` (`IncludeInCalculations`, `IsQuote`);

CREATE UNIQUE INDEX `IX_ClientKycPolicies_LegacyKycId` ON `ClientKycPolicies` (`LegacyKycId`);

CREATE INDEX `IX_ClientKycPolicies_LegacyMainClassId_LegacySubClassId` ON `ClientKycPolicies` (`LegacyMainClassId`, `LegacySubClassId`);

CREATE INDEX `IX_ClientKycPolicies_PolicyNumber` ON `ClientKycPolicies` (`PolicyNumber`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260531145538_AddClientKycPolicies', '10.0.8');

ALTER TABLE `ClientNotes` MODIFY `LegacyClientNoteId` int NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260531172421_MakeClientNotesOperational', '10.0.8');

CREATE TABLE `ClientInvestmentAccounts` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientId` int NOT NULL,
    `LegacyInvestmentAccountId` int NOT NULL,
    `LegacyClientId` int NULL,
    `InvestmentDate` date NULL,
    `SurrenderDate` date NULL,
    `Administrator` varchar(256) NULL,
    `LegacyAdministratorId` int NULL,
    `AccountNumber` varchar(256) NULL,
    `ProductName` varchar(256) NULL,
    `LegacyProductId` int NULL,
    `ProductType` varchar(256) NULL,
    `LegacyProductTypeId` int NULL,
    `FundName` varchar(256) NULL,
    `LegacyFundId` int NULL,
    `IsLinkedHead` tinyint(1) NOT NULL,
    `LegacyLinkedAccountId` int NULL,
    `IsFinal` tinyint(1) NOT NULL,
    `OpenedBy` varchar(256) NULL,
    `UpdatedBy` varchar(256) NULL,
    `LegacyOpenedByUserId` int NULL,
    `LegacyUpdatedByUserId` int NULL,
    `LegacyOpenedAt` datetime(6) NULL,
    `LegacyUpdatedAt` datetime(6) NULL,
    `PayloadJson` longtext NOT NULL,
    `ImportedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientInvestmentAccounts_Clients_ClientId` FOREIGN KEY (`ClientId`) REFERENCES `Clients` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `ClientInvestmentTransactions` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientInvestmentAccountId` int NOT NULL,
    `LegacyInvestmentHistoryId` int NOT NULL,
    `LegacyInvestmentAccountId` int NULL,
    `TransactionDate` date NULL,
    `Description` longtext NULL,
    `ExchangeRate` decimal(18,6) NULL,
    `InvestmentAmountForeign` decimal(18,2) NULL,
    `InvestmentAmountZar` decimal(18,2) NULL,
    `WithdrawalAmountForeign` decimal(18,2) NULL,
    `WithdrawalAmountZar` decimal(18,2) NULL,
    `InvestmentFrequency` varchar(100) NULL,
    `AnnualIncreasePercent` decimal(9,4) NULL,
    `BalanceForeign` decimal(18,2) NULL,
    `BalanceZar` decimal(18,2) NULL,
    `IsDeleted` tinyint(1) NOT NULL,
    `IsFinal` tinyint(1) NOT NULL,
    `OpenedBy` varchar(256) NULL,
    `UpdatedBy` varchar(256) NULL,
    `LegacyOpenedByUserId` int NULL,
    `LegacyUpdatedByUserId` int NULL,
    `LegacyOpenedAt` datetime(6) NULL,
    `LegacyUpdatedAt` datetime(6) NULL,
    `PayloadJson` longtext NOT NULL,
    `ImportedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientInvestmentTransactions_ClientInvestmentAccounts_Client~` FOREIGN KEY (`ClientInvestmentAccountId`) REFERENCES `ClientInvestmentAccounts` (`Id`) ON DELETE CASCADE
);

CREATE INDEX `IX_ClientInvestmentAccounts_AccountNumber` ON `ClientInvestmentAccounts` (`AccountNumber`);

CREATE INDEX `IX_ClientInvestmentAccounts_ClientId` ON `ClientInvestmentAccounts` (`ClientId`);

CREATE INDEX `IX_ClientInvestmentAccounts_LegacyClientId` ON `ClientInvestmentAccounts` (`LegacyClientId`);

CREATE UNIQUE INDEX `IX_ClientInvestmentAccounts_LegacyInvestmentAccountId` ON `ClientInvestmentAccounts` (`LegacyInvestmentAccountId`);

CREATE INDEX `IX_ClientInvestmentAccounts_LegacyLinkedAccountId` ON `ClientInvestmentAccounts` (`LegacyLinkedAccountId`);

CREATE INDEX `IX_ClientInvestmentTransactions_ClientInvestmentAccountId` ON `ClientInvestmentTransactions` (`ClientInvestmentAccountId`);

CREATE INDEX `IX_ClientInvestmentTransactions_LegacyInvestmentAccountId` ON `ClientInvestmentTransactions` (`LegacyInvestmentAccountId`);

CREATE UNIQUE INDEX `IX_ClientInvestmentTransactions_LegacyInvestmentHistoryId` ON `ClientInvestmentTransactions` (`LegacyInvestmentHistoryId`);

CREATE INDEX `IX_ClientInvestmentTransactions_TransactionDate` ON `ClientInvestmentTransactions` (`TransactionDate`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260531194916_AddClientInvestments', '10.0.8');

CREATE TABLE `ClientFundValuations` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientId` int NOT NULL,
    `LegacyFundId` int NOT NULL,
    `LegacyClientId` int NULL,
    `KanaanId` varchar(30) NULL,
    `FundName` varchar(256) NOT NULL,
    `AmountForeign` decimal(18,2) NULL,
    `AmountZar` decimal(18,2) NULL,
    `FundDescription` longtext NULL,
    `CompanyClientNumber` varchar(256) NULL,
    `Administrator` varchar(256) NULL,
    `ProductName` varchar(256) NULL,
    `ProductType` varchar(256) NULL,
    `CompanyDescription` longtext NULL,
    `InvestmentUniqueNumber` varchar(256) NULL,
    `ValuationDate` date NULL,
    `OpenedBy` varchar(256) NULL,
    `UpdatedBy` varchar(256) NULL,
    `LegacyOpenedByUserId` int NULL,
    `LegacyUpdatedByUserId` int NULL,
    `LegacyOpenedAt` datetime(6) NULL,
    `LegacyUpdatedAt` datetime(6) NULL,
    `PayloadJson` longtext NOT NULL,
    `ImportedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientFundValuations_Clients_ClientId` FOREIGN KEY (`ClientId`) REFERENCES `Clients` (`Id`) ON DELETE CASCADE
);

CREATE INDEX `IX_ClientFundValuations_ClientId` ON `ClientFundValuations` (`ClientId`);

CREATE INDEX `IX_ClientFundValuations_InvestmentUniqueNumber` ON `ClientFundValuations` (`InvestmentUniqueNumber`);

CREATE INDEX `IX_ClientFundValuations_KanaanId` ON `ClientFundValuations` (`KanaanId`);

CREATE INDEX `IX_ClientFundValuations_LegacyClientId` ON `ClientFundValuations` (`LegacyClientId`);

CREATE UNIQUE INDEX `IX_ClientFundValuations_LegacyFundId` ON `ClientFundValuations` (`LegacyFundId`);

CREATE INDEX `IX_ClientFundValuations_ValuationDate` ON `ClientFundValuations` (`ValuationDate`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260531204111_AddClientFundValuations', '10.0.8');

ALTER TABLE `ClientKycPolicies` MODIFY `LegacyKycId` int NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260601201901_MakeKycPoliciesOperational', '10.0.8');

DROP INDEX IX_ClientKycPolicies_LegacyKycId ON ClientKycPolicies;

ALTER TABLE `ClientInvestmentTransactions` MODIFY `LegacyInvestmentHistoryId` int NULL;

ALTER TABLE `ClientInvestmentAccounts` MODIFY `LegacyInvestmentAccountId` int NULL;

CREATE TABLE `ClientKycRecommendations` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientId` int NOT NULL,
    `ClientKycPolicyId` int NULL,
    `LegacyRecommendationId` int NULL,
    `LegacyClientId` int NULL,
    `KanaanId` varchar(256) NULL,
    `RecommendationType` varchar(256) NULL,
    `Status` varchar(256) NULL,
    `RecommendationDate` date NULL,
    `Details` longtext NULL,
    `Outcome` longtext NULL,
    `OpenedBy` varchar(256) NULL,
    `UpdatedBy` varchar(256) NULL,
    `PayloadJson` longtext NOT NULL,
    `ImportedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientKycRecommendations_ClientKycPolicies_ClientKycPolicyId` FOREIGN KEY (`ClientKycPolicyId`) REFERENCES `ClientKycPolicies` (`Id`) ON DELETE SET NULL,
    CONSTRAINT `FK_ClientKycRecommendations_Clients_ClientId` FOREIGN KEY (`ClientId`) REFERENCES `Clients` (`Id`) ON DELETE CASCADE
);

CREATE INDEX `IX_ClientKycPolicies_LegacyKycId` ON `ClientKycPolicies` (`LegacyKycId`);

CREATE INDEX `IX_ClientKycRecommendations_ClientId` ON `ClientKycRecommendations` (`ClientId`);

CREATE INDEX `IX_ClientKycRecommendations_ClientKycPolicyId` ON `ClientKycRecommendations` (`ClientKycPolicyId`);

CREATE INDEX `IX_ClientKycRecommendations_KanaanId` ON `ClientKycRecommendations` (`KanaanId`);

CREATE INDEX `IX_ClientKycRecommendations_LegacyRecommendationId` ON `ClientKycRecommendations` (`LegacyRecommendationId`);

CREATE INDEX `IX_ClientKycRecommendations_Status` ON `ClientKycRecommendations` (`Status`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260602211709_CompleteOutstandingWorkflows', '10.0.8');

CREATE TABLE `InvestmentAdministratorReferences` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `LegacyLispId` int NULL,
    `Name` varchar(256) NOT NULL,
    `ShortName` varchar(256) NULL,
    `IsCurrent` tinyint(1) NOT NULL,
    `MonthlyUpload` tinyint(1) NOT NULL,
    `OpenedBy` varchar(256) NULL,
    `UpdatedBy` varchar(256) NULL,
    `LegacyOpenedAt` datetime(6) NULL,
    `LegacyUpdatedAt` datetime(6) NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `InvestmentFundReferences` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `LegacyFundNameId` int NULL,
    `Name` varchar(256) NOT NULL,
    `ShortName` varchar(256) NULL,
    `Currency` varchar(32) NULL,
    `IsCurrent` tinyint(1) NOT NULL,
    `MonthlyUpload` tinyint(1) NOT NULL,
    `LegacyMainClassId` int NULL,
    `LegacySubClassId` int NULL,
    `LegacyAdministratorId` int NULL,
    `OpenedBy` varchar(256) NULL,
    `UpdatedBy` varchar(256) NULL,
    `LegacyOpenedAt` datetime(6) NULL,
    `LegacyUpdatedAt` datetime(6) NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `InvestmentProductTypeReferences` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `LegacyCompanyProductId` int NULL,
    `Name` varchar(256) NOT NULL,
    `OpenedBy` varchar(256) NULL,
    `UpdatedBy` varchar(256) NULL,
    `LegacyOpenedAt` datetime(6) NULL,
    `LegacyUpdatedAt` datetime(6) NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `KycMainClassReferences` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `LegacyMainClassId` int NULL,
    `Name` varchar(256) NOT NULL,
    `AfrikaansDescription` varchar(512) NULL,
    `EnglishDescription` varchar(512) NULL,
    `OpenedBy` varchar(256) NULL,
    `UpdatedBy` varchar(256) NULL,
    `LegacyOpenedAt` datetime(6) NULL,
    `LegacyUpdatedAt` datetime(6) NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `MarketReferenceValues` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `LegacyMiscInfoId` int NULL,
    `PriceDate` date NULL,
    `Name` varchar(256) NOT NULL,
    `Value` decimal(18,4) NULL,
    `OpenedBy` varchar(256) NULL,
    `UpdatedBy` varchar(256) NULL,
    `LegacyOpenedAt` datetime(6) NULL,
    `LegacyUpdatedAt` datetime(6) NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `KycSubClassReferences` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `LegacySubClassId` int NULL,
    `KycMainClassReferenceId` int NOT NULL,
    `LegacyMainClassId` int NULL,
    `Name` varchar(256) NOT NULL,
    `OpenedBy` varchar(256) NULL,
    `UpdatedBy` varchar(256) NULL,
    `LegacyOpenedAt` datetime(6) NULL,
    `LegacyUpdatedAt` datetime(6) NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_KycSubClassReferences_KycMainClassReferences_KycMainClassRef~` FOREIGN KEY (`KycMainClassReferenceId`) REFERENCES `KycMainClassReferences` (`Id`) ON DELETE CASCADE
);

CREATE INDEX `IX_InvestmentAdministratorReferences_IsCurrent_Name` ON `InvestmentAdministratorReferences` (`IsCurrent`, `Name`);

CREATE UNIQUE INDEX `IX_InvestmentAdministratorReferences_LegacyLispId` ON `InvestmentAdministratorReferences` (`LegacyLispId`);

CREATE INDEX `IX_InvestmentAdministratorReferences_Name` ON `InvestmentAdministratorReferences` (`Name`);

CREATE INDEX `IX_InvestmentFundReferences_IsCurrent_Name` ON `InvestmentFundReferences` (`IsCurrent`, `Name`);

CREATE INDEX `IX_InvestmentFundReferences_LegacyAdministratorId_LegacyMainCla~` ON `InvestmentFundReferences` (`LegacyAdministratorId`, `LegacyMainClassId`, `LegacySubClassId`);

CREATE UNIQUE INDEX `IX_InvestmentFundReferences_LegacyFundNameId` ON `InvestmentFundReferences` (`LegacyFundNameId`);

CREATE INDEX `IX_InvestmentFundReferences_Name` ON `InvestmentFundReferences` (`Name`);

CREATE INDEX `IX_InvestmentFundReferences_ShortName` ON `InvestmentFundReferences` (`ShortName`);

CREATE UNIQUE INDEX `IX_InvestmentProductTypeReferences_LegacyCompanyProductId` ON `InvestmentProductTypeReferences` (`LegacyCompanyProductId`);

CREATE INDEX `IX_InvestmentProductTypeReferences_Name` ON `InvestmentProductTypeReferences` (`Name`);

CREATE UNIQUE INDEX `IX_KycMainClassReferences_LegacyMainClassId` ON `KycMainClassReferences` (`LegacyMainClassId`);

CREATE INDEX `IX_KycMainClassReferences_Name` ON `KycMainClassReferences` (`Name`);

CREATE INDEX `IX_KycSubClassReferences_KycMainClassReferenceId_Name` ON `KycSubClassReferences` (`KycMainClassReferenceId`, `Name`);

CREATE INDEX `IX_KycSubClassReferences_LegacyMainClassId` ON `KycSubClassReferences` (`LegacyMainClassId`);

CREATE UNIQUE INDEX `IX_KycSubClassReferences_LegacySubClassId` ON `KycSubClassReferences` (`LegacySubClassId`);

CREATE UNIQUE INDEX `IX_MarketReferenceValues_LegacyMiscInfoId` ON `MarketReferenceValues` (`LegacyMiscInfoId`);

CREATE INDEX `IX_MarketReferenceValues_Name` ON `MarketReferenceValues` (`Name`);

CREATE INDEX `IX_MarketReferenceValues_PriceDate` ON `MarketReferenceValues` (`PriceDate`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260607203500_AddReferenceDataClosure', '10.0.8');

COMMIT;

