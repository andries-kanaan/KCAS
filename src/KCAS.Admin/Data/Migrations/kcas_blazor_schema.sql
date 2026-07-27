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
VALUES ('20260529223052_InitialKcasSchema', '10.0.10');

ALTER TABLE `AspNetUsers` ADD `ApprovedAtUtc` datetime(6) NULL;

ALTER TABLE `AspNetUsers` ADD `ApprovedByUserId` varchar(64) NULL;

ALTER TABLE `AspNetUsers` ADD `CreatedAtUtc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6);

ALTER TABLE `AspNetUsers` ADD `DisplayName` varchar(191) NULL;

ALTER TABLE `AspNetUsers` ADD `IsApproved` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `AspNetUsers` ADD `WindowsAccountName` varchar(191) NULL;

CREATE INDEX `IX_AspNetUsers_WindowsAccountName` ON `AspNetUsers` (`WindowsAccountName`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260531080449_AddSecurityRbac', '10.0.10');

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
VALUES ('20260531130752_AddNormalizedClientImport', '10.0.10');

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
VALUES ('20260531132831_SurfaceLegacyClientSections', '10.0.10');

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
VALUES ('20260531135015_AddClientNotes', '10.0.10');

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
VALUES ('20260531145538_AddClientKycPolicies', '10.0.10');

ALTER TABLE `ClientNotes` MODIFY `LegacyClientNoteId` int NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260531172421_MakeClientNotesOperational', '10.0.10');

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
VALUES ('20260531194916_AddClientInvestments', '10.0.10');

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
VALUES ('20260531204111_AddClientFundValuations', '10.0.10');

ALTER TABLE `ClientKycPolicies` MODIFY `LegacyKycId` int NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260601201901_MakeKycPoliciesOperational', '10.0.10');

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
VALUES ('20260602211709_CompleteOutstandingWorkflows', '10.0.10');

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
VALUES ('20260607203500_AddReferenceDataClosure', '10.0.10');

ALTER TABLE `Clients` ADD `LegacyReconciliationStatus` varchar(32) NOT NULL DEFAULT 'Unscanned';

CREATE TABLE `LegacyImportRuns` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `Mode` varchar(32) NOT NULL,
    `Status` varchar(32) NOT NULL,
    `SourceLabel` varchar(256) NOT NULL,
    `StartedAtUtc` datetime(6) NOT NULL,
    `CompletedAtUtc` datetime(6) NULL,
    `NewCount` int NOT NULL,
    `UnchangedCount` int NOT NULL,
    `ChangedCount` int NOT NULL,
    `MissingCount` int NOT NULL,
    `InvalidCount` int NOT NULL,
    `OrphanedCount` int NOT NULL,
    `AppliedCount` int NOT NULL,
    `FailedCount` int NOT NULL,
    `ErrorSummary` longtext NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `LegacySourceSnapshots` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `SourceTable` varchar(64) NOT NULL,
    `SourceId` bigint NOT NULL,
    `Fingerprint` varchar(64) NOT NULL,
    `PayloadJson` longtext NOT NULL,
    `AcceptedAtUtc` datetime(6) NOT NULL,
    `AcceptedFromRunId` bigint NULL,
    `LastSeenAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `LegacyImportRowStates` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `LegacyImportRunId` bigint NOT NULL,
    `SourceTable` varchar(64) NOT NULL,
    `SourceId` bigint NOT NULL,
    `Classification` varchar(32) NOT NULL,
    `ApplyStatus` varchar(32) NOT NULL,
    `TargetEntityType` varchar(128) NULL,
    `TargetEntityId` bigint NULL,
    `IncomingFingerprint` varchar(64) NOT NULL,
    `BaselineFingerprint` varchar(64) NULL,
    `IncomingPayloadJson` longtext NOT NULL,
    `BaselinePayloadJson` longtext NULL,
    `SourceUpdatedAt` datetime(6) NULL,
    `Error` longtext NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_LegacyImportRowStates_LegacyImportRuns_LegacyImportRunId` FOREIGN KEY (`LegacyImportRunId`) REFERENCES `LegacyImportRuns` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `LegacyImportDifferences` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `LegacyImportRowStateId` bigint NOT NULL,
    `FieldName` varchar(191) NOT NULL,
    `BaselineValue` longtext NULL,
    `IncomingValue` longtext NULL,
    `Decision` varchar(32) NOT NULL,
    `ResolvedValue` longtext NULL,
    `ReviewedBy` varchar(191) NULL,
    `ReviewedAtUtc` datetime(6) NULL,
    `ReviewReason` longtext NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_LegacyImportDifferences_LegacyImportRowStates_LegacyImportRo~` FOREIGN KEY (`LegacyImportRowStateId`) REFERENCES `LegacyImportRowStates` (`Id`) ON DELETE CASCADE
);

CREATE INDEX `IX_LegacyImportDifferences_Decision` ON `LegacyImportDifferences` (`Decision`);

CREATE UNIQUE INDEX `IX_LegacyImportDifferences_LegacyImportRowStateId_FieldName` ON `LegacyImportDifferences` (`LegacyImportRowStateId`, `FieldName`);

CREATE INDEX `IX_LegacyImportRowStates_LegacyImportRunId_Classification` ON `LegacyImportRowStates` (`LegacyImportRunId`, `Classification`);

CREATE UNIQUE INDEX `IX_LegacyImportRowStates_LegacyImportRunId_SourceTable_SourceId` ON `LegacyImportRowStates` (`LegacyImportRunId`, `SourceTable`, `SourceId`);

CREATE INDEX `IX_LegacyImportRuns_StartedAtUtc` ON `LegacyImportRuns` (`StartedAtUtc`);

CREATE INDEX `IX_LegacyImportRuns_Status` ON `LegacyImportRuns` (`Status`);

CREATE INDEX `IX_LegacySourceSnapshots_LastSeenAtUtc` ON `LegacySourceSnapshots` (`LastSeenAtUtc`);

CREATE UNIQUE INDEX `IX_LegacySourceSnapshots_SourceTable_SourceId` ON `LegacySourceSnapshots` (`SourceTable`, `SourceId`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260722094552_AddIncrementalLegacyReconciliation', '10.0.10');

ALTER TABLE `LegacyImportRuns` ADD `ApprovedScanRunId` bigint NULL;

ALTER TABLE `LegacyImportRuns` ADD `SourceSnapshotFileName` varchar(260) NULL;

ALTER TABLE `LegacyImportRuns` ADD `SourceSnapshotSha256` varchar(64) NOT NULL DEFAULT '';

CREATE INDEX `IX_LegacyImportRuns_SourceSnapshotSha256` ON `LegacyImportRuns` (`SourceSnapshotSha256`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260722132929_AddLegacyImportSnapshotProvenance', '10.0.10');

CREATE TABLE `ComplianceApprovals` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `TargetEntityType` varchar(128) NOT NULL,
    `TargetEntityId` int NOT NULL,
    `Decision` varchar(32) NOT NULL,
    `Approver` varchar(191) NULL,
    `DecidedAtUtc` datetime(6) NOT NULL,
    `Reason` longtext NOT NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `ComplianceAuditEvents` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `EntityType` varchar(128) NOT NULL,
    `EntityId` int NOT NULL,
    `Action` varchar(64) NOT NULL,
    `OldValueJson` longtext NULL,
    `NewValueJson` longtext NULL,
    `UserName` varchar(191) NULL,
    `TimestampUtc` datetime(6) NOT NULL,
    `Reason` longtext NOT NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `ComplianceEvidence` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `EvidenceType` varchar(96) NOT NULL,
    `Title` varchar(240) NOT NULL,
    `Source` varchar(191) NULL,
    `Location` longtext NULL,
    `ReceivedDate` date NULL,
    `VerifiedDate` date NULL,
    `ExpiryDate` date NULL,
    `Reviewer` varchar(191) NULL,
    `Notes` longtext NULL,
    `LinkedEntityType` varchar(128) NULL,
    `LinkedEntityId` int NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NULL,
    `UpdatedBy` varchar(191) NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `ComplianceProfiles` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `LegalName` varchar(200) NOT NULL,
    `TradingName` varchar(200) NULL,
    `FspNumber` varchar(64) NULL,
    `AccountableInstitutionNumber` varchar(64) NULL,
    `PrimaryContactName` varchar(191) NULL,
    `PrimaryContactEmail` varchar(191) NULL,
    `PrimaryContactPhone` varchar(64) NULL,
    `RegisteredAddress` longtext NULL,
    `OperatingAddress` longtext NULL,
    `EffectiveFrom` date NULL,
    `EffectiveTo` date NULL,
    `Status` varchar(32) NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NULL,
    `UpdatedBy` varchar(191) NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `ComplianceReferenceValues` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Category` varchar(96) NOT NULL,
    `Code` varchar(96) NOT NULL,
    `Name` varchar(191) NOT NULL,
    `Description` longtext NULL,
    `SortOrder` int NOT NULL,
    `IsActive` tinyint(1) NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NULL,
    `UpdatedBy` varchar(191) NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `ComplianceTasks` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Title` varchar(240) NOT NULL,
    `Description` longtext NULL,
    `Owner` varchar(191) NULL,
    `DueDate` date NULL,
    `Priority` varchar(32) NOT NULL,
    `Status` varchar(32) NOT NULL,
    `LinkedEntityType` varchar(128) NULL,
    `LinkedEntityId` int NULL,
    `ClosureNotes` longtext NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `ClosedAtUtc` datetime(6) NULL,
    `UpdatedAtUtc` datetime(6) NULL,
    `UpdatedBy` varchar(191) NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `ControlledDocuments` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `DocumentType` varchar(96) NOT NULL,
    `Title` varchar(240) NOT NULL,
    `Owner` varchar(191) NULL,
    `VersionReference` varchar(96) NULL,
    `Status` varchar(32) NOT NULL,
    `EffectiveDate` date NULL,
    `NextReviewDate` date NULL,
    `Location` longtext NULL,
    `Notes` longtext NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NULL,
    `UpdatedBy` varchar(191) NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `GovernanceRoleAssignments` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `RoleType` varchar(96) NOT NULL,
    `PersonName` varchar(191) NOT NULL,
    `Email` varchar(191) NULL,
    `Phone` varchar(64) NULL,
    `ResponsibilitySummary` longtext NULL,
    `StartDate` date NULL,
    `EndDate` date NULL,
    `IsActive` tinyint(1) NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NULL,
    `UpdatedBy` varchar(191) NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `RiskMethodologyVersions` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Name` varchar(191) NOT NULL,
    `VersionLabel` varchar(64) NULL,
    `Status` varchar(32) NOT NULL,
    `EffectiveFrom` date NULL,
    `EffectiveTo` date NULL,
    `Summary` longtext NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `SubmittedAtUtc` datetime(6) NULL,
    `ApprovedAtUtc` datetime(6) NULL,
    `ActivatedAtUtc` datetime(6) NULL,
    `UpdatedBy` varchar(191) NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `RiskBands` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `RiskMethodologyVersionId` int NOT NULL,
    `Name` varchar(96) NOT NULL,
    `MinimumScore` decimal(9,4) NOT NULL,
    `MaximumScore` decimal(9,4) NULL,
    `SortOrder` int NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_RiskBands_RiskMethodologyVersions_RiskMethodologyVersionId` FOREIGN KEY (`RiskMethodologyVersionId`) REFERENCES `RiskMethodologyVersions` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `RiskFactorDefinitions` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `RiskMethodologyVersionId` int NOT NULL,
    `Code` varchar(96) NOT NULL,
    `Name` varchar(191) NOT NULL,
    `Description` longtext NULL,
    `Weight` decimal(9,4) NOT NULL,
    `IsMandatoryHighRiskTrigger` tinyint(1) NOT NULL,
    `SortOrder` int NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_RiskFactorDefinitions_RiskMethodologyVersions_RiskMethodolog~` FOREIGN KEY (`RiskMethodologyVersionId`) REFERENCES `RiskMethodologyVersions` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `RiskFactorOptions` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `RiskFactorDefinitionId` int NOT NULL,
    `Code` varchar(96) NOT NULL,
    `Label` varchar(191) NOT NULL,
    `Score` int NOT NULL,
    `TriggersHighRisk` tinyint(1) NOT NULL,
    `SortOrder` int NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_RiskFactorOptions_RiskFactorDefinitions_RiskFactorDefinition~` FOREIGN KEY (`RiskFactorDefinitionId`) REFERENCES `RiskFactorDefinitions` (`Id`) ON DELETE CASCADE
);

CREATE INDEX `IX_ComplianceApprovals_TargetEntityType_TargetEntityId` ON `ComplianceApprovals` (`TargetEntityType`, `TargetEntityId`);

CREATE INDEX `IX_ComplianceAuditEvents_EntityType_EntityId` ON `ComplianceAuditEvents` (`EntityType`, `EntityId`);

CREATE INDEX `IX_ComplianceAuditEvents_TimestampUtc` ON `ComplianceAuditEvents` (`TimestampUtc`);

CREATE INDEX `IX_ComplianceEvidence_EvidenceType_ExpiryDate` ON `ComplianceEvidence` (`EvidenceType`, `ExpiryDate`);

CREATE INDEX `IX_ComplianceEvidence_LinkedEntityType_LinkedEntityId` ON `ComplianceEvidence` (`LinkedEntityType`, `LinkedEntityId`);

CREATE INDEX `IX_ComplianceProfiles_Status` ON `ComplianceProfiles` (`Status`);

CREATE UNIQUE INDEX `IX_ComplianceReferenceValues_Category_Code_IsActive` ON `ComplianceReferenceValues` (`Category`, `Code`, `IsActive`);

CREATE INDEX `IX_ComplianceReferenceValues_Category_SortOrder` ON `ComplianceReferenceValues` (`Category`, `SortOrder`);

CREATE INDEX `IX_ComplianceTasks_LinkedEntityType_LinkedEntityId` ON `ComplianceTasks` (`LinkedEntityType`, `LinkedEntityId`);

CREATE INDEX `IX_ComplianceTasks_Status_DueDate` ON `ComplianceTasks` (`Status`, `DueDate`);

CREATE INDEX `IX_ControlledDocuments_DocumentType_Status` ON `ControlledDocuments` (`DocumentType`, `Status`);

CREATE INDEX `IX_ControlledDocuments_NextReviewDate` ON `ControlledDocuments` (`NextReviewDate`);

CREATE INDEX `IX_GovernanceRoleAssignments_RoleType_IsActive` ON `GovernanceRoleAssignments` (`RoleType`, `IsActive`);

CREATE UNIQUE INDEX `IX_RiskBands_RiskMethodologyVersionId_Name` ON `RiskBands` (`RiskMethodologyVersionId`, `Name`);

CREATE UNIQUE INDEX `IX_RiskFactorDefinitions_RiskMethodologyVersionId_Code` ON `RiskFactorDefinitions` (`RiskMethodologyVersionId`, `Code`);

CREATE UNIQUE INDEX `IX_RiskFactorOptions_RiskFactorDefinitionId_Code` ON `RiskFactorOptions` (`RiskFactorDefinitionId`, `Code`);

CREATE INDEX `IX_RiskMethodologyVersions_EffectiveFrom` ON `RiskMethodologyVersions` (`EffectiveFrom`);

CREATE INDEX `IX_RiskMethodologyVersions_Status` ON `RiskMethodologyVersions` (`Status`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260723084233_AddComplianceFoundation', '10.0.10');

CREATE TABLE `ClientEvidenceRequirements` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientCategory` varchar(96) NOT NULL,
    `RequirementGroup` varchar(96) NOT NULL,
    `EvidenceType` varchar(96) NOT NULL,
    `Title` varchar(240) NOT NULL,
    `Description` longtext NULL,
    `IsBlocking` tinyint(1) NOT NULL,
    `RequiresVerification` tinyint(1) NOT NULL,
    `RequiresExpiryDate` tinyint(1) NOT NULL,
    `SortOrder` int NOT NULL,
    `Status` varchar(32) NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NULL,
    `UpdatedBy` varchar(191) NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `ClientEvidenceScanRoots` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `RootPath` varchar(512) NOT NULL,
    `IsActive` tinyint(1) NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NULL,
    `UpdatedBy` varchar(191) NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `ClientEvidenceScanRuns` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `RootPath` varchar(512) NOT NULL,
    `StartedAtUtc` datetime(6) NOT NULL,
    `FinishedAtUtc` datetime(6) NULL,
    `Status` varchar(32) NOT NULL,
    `TotalFiles` int NOT NULL,
    `LinkedFiles` int NOT NULL,
    `UnmatchedFiles` int NOT NULL,
    `AmbiguousFiles` int NOT NULL,
    `SkippedFiles` int NOT NULL,
    `ErrorMessage` longtext NULL,
    `StartedBy` varchar(191) NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `ClientEvidenceExceptions` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientId` int NOT NULL,
    `ClientEvidenceRequirementId` int NOT NULL,
    `Reason` longtext NOT NULL,
    `ApprovedBy` varchar(191) NOT NULL,
    `ApprovedAtUtc` datetime(6) NOT NULL,
    `ReviewDate` date NULL,
    `IsActive` tinyint(1) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientEvidenceExceptions_ClientEvidenceRequirements_ClientEv~` FOREIGN KEY (`ClientEvidenceRequirementId`) REFERENCES `ClientEvidenceRequirements` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_ClientEvidenceExceptions_Clients_ClientId` FOREIGN KEY (`ClientId`) REFERENCES `Clients` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `ClientEvidenceScanFiles` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientEvidenceScanRunId` int NOT NULL,
    `ClientId` int NULL,
    `FullPath` varchar(512) NOT NULL,
    `RelativePath` varchar(512) NOT NULL,
    `FileName` varchar(260) NOT NULL,
    `FileSha256` varchar(64) NOT NULL,
    `FileSizeBytes` bigint NOT NULL,
    `FileLastWriteTimeUtc` datetime(6) NOT NULL,
    `MatchStatus` varchar(32) NOT NULL,
    `SuggestedEvidenceType` varchar(96) NULL,
    `MatchReason` varchar(512) NULL,
    `CandidateCount` int NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientEvidenceScanFiles_ClientEvidenceScanRuns_ClientEvidenc~` FOREIGN KEY (`ClientEvidenceScanRunId`) REFERENCES `ClientEvidenceScanRuns` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_ClientEvidenceScanFiles_Clients_ClientId` FOREIGN KEY (`ClientId`) REFERENCES `Clients` (`Id`) ON DELETE SET NULL
);

CREATE TABLE `ClientEvidenceItems` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientId` int NOT NULL,
    `ClientEvidenceRequirementId` int NULL,
    `EvidenceType` varchar(96) NOT NULL,
    `Title` varchar(240) NOT NULL,
    `SourcePath` varchar(512) NULL,
    `RelativePath` varchar(512) NULL,
    `FileName` varchar(260) NULL,
    `FileSha256` varchar(64) NULL,
    `FileSizeBytes` bigint NULL,
    `FileLastWriteTimeUtc` datetime(6) NULL,
    `ReceivedDate` date NULL,
    `VerifiedDate` date NULL,
    `ExpiryDate` date NULL,
    `Reviewer` varchar(191) NULL,
    `Status` varchar(32) NOT NULL,
    `Notes` longtext NULL,
    `ClientEvidenceScanFileId` int NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NULL,
    `UpdatedBy` varchar(191) NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientEvidenceItems_ClientEvidenceRequirements_ClientEvidenc~` FOREIGN KEY (`ClientEvidenceRequirementId`) REFERENCES `ClientEvidenceRequirements` (`Id`) ON DELETE SET NULL,
    CONSTRAINT `FK_ClientEvidenceItems_ClientEvidenceScanFiles_ClientEvidenceSc~` FOREIGN KEY (`ClientEvidenceScanFileId`) REFERENCES `ClientEvidenceScanFiles` (`Id`) ON DELETE SET NULL,
    CONSTRAINT `FK_ClientEvidenceItems_Clients_ClientId` FOREIGN KEY (`ClientId`) REFERENCES `Clients` (`Id`) ON DELETE CASCADE
);

CREATE INDEX `IX_ClientEvidenceExceptions_ClientEvidenceRequirementId` ON `ClientEvidenceExceptions` (`ClientEvidenceRequirementId`);

CREATE INDEX `IX_ClientEvidenceExceptions_ClientId_ClientEvidenceRequirementI~` ON `ClientEvidenceExceptions` (`ClientId`, `ClientEvidenceRequirementId`, `IsActive`);

CREATE INDEX `IX_ClientEvidenceExceptions_ReviewDate` ON `ClientEvidenceExceptions` (`ReviewDate`);

CREATE INDEX `IX_ClientEvidenceItems_ClientEvidenceRequirementId` ON `ClientEvidenceItems` (`ClientEvidenceRequirementId`);

CREATE UNIQUE INDEX `IX_ClientEvidenceItems_ClientEvidenceScanFileId` ON `ClientEvidenceItems` (`ClientEvidenceScanFileId`);

CREATE INDEX `IX_ClientEvidenceItems_ClientId_EvidenceType_Status` ON `ClientEvidenceItems` (`ClientId`, `EvidenceType`, `Status`);

CREATE INDEX `IX_ClientEvidenceItems_ExpiryDate` ON `ClientEvidenceItems` (`ExpiryDate`);

CREATE INDEX `IX_ClientEvidenceItems_FileSha256` ON `ClientEvidenceItems` (`FileSha256`);

CREATE INDEX `IX_ClientEvidenceRequirements_ClientCategory_EvidenceType_Status` ON `ClientEvidenceRequirements` (`ClientCategory`, `EvidenceType`, `Status`);

CREATE INDEX `IX_ClientEvidenceRequirements_RequirementGroup_SortOrder` ON `ClientEvidenceRequirements` (`RequirementGroup`, `SortOrder`);

CREATE INDEX `IX_ClientEvidenceScanFiles_ClientEvidenceScanRunId_MatchStatus` ON `ClientEvidenceScanFiles` (`ClientEvidenceScanRunId`, `MatchStatus`);

CREATE INDEX `IX_ClientEvidenceScanFiles_ClientId` ON `ClientEvidenceScanFiles` (`ClientId`);

CREATE INDEX `IX_ClientEvidenceScanFiles_FileSha256` ON `ClientEvidenceScanFiles` (`FileSha256`);

CREATE INDEX `IX_ClientEvidenceScanRoots_IsActive` ON `ClientEvidenceScanRoots` (`IsActive`);

CREATE INDEX `IX_ClientEvidenceScanRuns_StartedAtUtc` ON `ClientEvidenceScanRuns` (`StartedAtUtc`);

CREATE INDEX `IX_ClientEvidenceScanRuns_Status` ON `ClientEvidenceScanRuns` (`Status`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260723125704_AddClientEvidenceReadiness', '10.0.10');

ALTER TABLE `Clients` ADD `ClientCategory` varchar(96) NOT NULL DEFAULT 'NaturalPerson';

CREATE INDEX `IX_Clients_ClientCategory` ON `Clients` (`ClientCategory`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260723130255_AddClientEvidenceCategories', '10.0.10');

ALTER TABLE `ClientEvidenceItems` ADD `EscalationRequired` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `ClientEvidenceItems` ADD `ScreeningOutcome` varchar(96) NULL;

ALTER TABLE `ClientEvidenceItems` ADD `ScreeningReviewDate` date NULL;

ALTER TABLE `ClientEvidenceItems` ADD `ScreeningRiskSignal` varchar(32) NULL;

ALTER TABLE `ClientEvidenceItems` ADD `ScreeningSubjectName` varchar(240) NULL;

ALTER TABLE `ClientEvidenceItems` ADD `ScreeningSubjectType` varchar(96) NULL;

CREATE INDEX `IX_ClientEvidenceItems_ClientId_EvidenceType_ScreeningRiskSignal` ON `ClientEvidenceItems` (`ClientId`, `EvidenceType`, `ScreeningRiskSignal`);

CREATE INDEX `IX_ClientEvidenceItems_EscalationRequired` ON `ClientEvidenceItems` (`EscalationRequired`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260723191721_AddClientEvidenceScreeningReviews', '10.0.10');

ALTER TABLE `Clients` ADD `ClientCategoryReason` varchar(512) NULL;

ALTER TABLE `Clients` ADD `ClientCategorySource` varchar(32) NOT NULL DEFAULT 'Unknown';

ALTER TABLE `Clients` ADD `ClientCategoryUpdatedAtUtc` datetime(6) NULL;

ALTER TABLE `Clients` ADD `ClientCategoryUpdatedBy` varchar(191) NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260724065033_AddClientCategoryProvenance', '10.0.10');

ALTER TABLE `ClientEvidenceItems` ADD `SelectedAtUtc` datetime(6) NULL;

ALTER TABLE `ClientEvidenceItems` ADD `SelectedBy` varchar(191) NULL;

ALTER TABLE `ClientEvidenceItems` ADD `SelectionConfidence` int NULL;

ALTER TABLE `ClientEvidenceItems` ADD `SelectionReason` varchar(512) NULL;

ALTER TABLE `ClientEvidenceItems` ADD `SelectionStatus` varchar(32) NOT NULL DEFAULT 'Candidate';

ALTER TABLE `ClientEvidenceItems` ADD `SupersededByClientEvidenceItemId` int NULL;

ALTER TABLE `ClientEvidenceItems` ADD `VerificationPolicy` varchar(32) NOT NULL DEFAULT 'ManualRequired';

CREATE INDEX `IX_ClientEvidenceItems_ClientId_EvidenceType_SelectionStatus` ON `ClientEvidenceItems` (`ClientId`, `EvidenceType`, `SelectionStatus`);

CREATE INDEX `IX_ClientEvidenceItems_SupersededByClientEvidenceItemId` ON `ClientEvidenceItems` (`SupersededByClientEvidenceItemId`);

ALTER TABLE `ClientEvidenceItems` ADD CONSTRAINT `FK_CEI_SupersededBy` FOREIGN KEY (`SupersededByClientEvidenceItemId`) REFERENCES `ClientEvidenceItems` (`Id`) ON DELETE SET NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260724165247_AddClientEvidenceSelectionState', '10.0.10');

ALTER TABLE `ClientEvidenceItems` ADD `OwnershipConfidence` int NULL;

ALTER TABLE `ClientEvidenceItems` ADD `OwnershipReason` varchar(512) NULL;

ALTER TABLE `ClientEvidenceItems` ADD `OwnershipReviewedAtUtc` datetime(6) NULL;

ALTER TABLE `ClientEvidenceItems` ADD `OwnershipReviewedBy` varchar(191) NULL;

ALTER TABLE `ClientEvidenceItems` ADD `OwnershipStatus` varchar(32) NOT NULL DEFAULT 'Confirmed';

CREATE TABLE `ClientEvidenceOwnershipAliases` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientId` int NOT NULL,
    `FolderPath` varchar(512) NOT NULL,
    `Alias` varchar(160) NOT NULL,
    `IsJoint` tinyint(1) NOT NULL,
    `IsActive` tinyint(1) NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `CreatedBy` varchar(191) NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientEvidenceOwnershipAliases_Clients_ClientId` FOREIGN KEY (`ClientId`) REFERENCES `Clients` (`Id`) ON DELETE CASCADE
);

CREATE INDEX `IX_ClientEvidenceItems_ClientId_OwnershipStatus` ON `ClientEvidenceItems` (`ClientId`, `OwnershipStatus`);

CREATE UNIQUE INDEX `IX_ClientEvidenceOwnershipAliases_ClientId_FolderPath_Alias` ON `ClientEvidenceOwnershipAliases` (`ClientId`, `FolderPath`, `Alias`);

CREATE INDEX `IX_ClientEvidenceOwnershipAliases_FolderPath_IsActive` ON `ClientEvidenceOwnershipAliases` (`FolderPath`, `IsActive`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260726085802_AddSharedEvidenceOwnership', '10.0.10');

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

ALTER TABLE `RiskBands` ADD `ReviewMonths` int NOT NULL DEFAULT 36;

CREATE TABLE `ClientRiskAssessments` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientId` int NOT NULL,
    `RiskMethodologyVersionId` int NOT NULL,
    `Status` varchar(32) NOT NULL,
    `CalculatedScore` decimal(9,4) NOT NULL,
    `CalculatedRating` varchar(96) NULL,
    `FinalRating` varchar(96) NULL,
    `IsOverride` tinyint(1) NOT NULL,
    `OverrideReason` longtext NULL,
    `HasPepExposure` tinyint(1) NOT NULL,
    `HasSanctionsConcern` tinyint(1) NOT NULL,
    `HasAdverseInformation` tinyint(1) NOT NULL,
    `RequiresEdd` tinyint(1) NOT NULL,
    `StandardControlsApplied` tinyint(1) NOT NULL,
    `Narrative` longtext NULL,
    `EffectiveDate` date NULL,
    `NextReviewDate` date NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    `FinalisedAtUtc` datetime(6) NULL,
    `ApprovedAtUtc` datetime(6) NULL,
    `PreparedBy` varchar(191) NULL,
    `FinalisedBy` varchar(191) NULL,
    `SnapshotJson` longtext NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientRiskAssessments_Clients_ClientId` FOREIGN KEY (`ClientId`) REFERENCES `Clients` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_ClientRiskAssessments_RiskMethodologyVersions_RiskMethodolog~` FOREIGN KEY (`RiskMethodologyVersionId`) REFERENCES `RiskMethodologyVersions` (`Id`) ON DELETE RESTRICT
);

CREATE TABLE `ClientRiskAssessmentApprovals` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientRiskAssessmentId` int NOT NULL,
    `Approver` varchar(191) NOT NULL,
    `Decision` varchar(32) NOT NULL,
    `Reason` longtext NOT NULL,
    `DecidedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientRiskAssessmentApprovals_ClientRiskAssessments_ClientRi~` FOREIGN KEY (`ClientRiskAssessmentId`) REFERENCES `ClientRiskAssessments` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `ClientRiskAssessmentResponses` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientRiskAssessmentId` int NOT NULL,
    `RiskFactorDefinitionId` int NOT NULL,
    `RiskFactorOptionId` int NULL,
    `ClientEvidenceItemId` int NULL,
    `Score` int NOT NULL,
    `WeightedScore` decimal(9,4) NOT NULL,
    `Explanation` longtext NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientRiskAssessmentResponses_ClientEvidenceItems_ClientEvid~` FOREIGN KEY (`ClientEvidenceItemId`) REFERENCES `ClientEvidenceItems` (`Id`) ON DELETE SET NULL,
    CONSTRAINT `FK_ClientRiskAssessmentResponses_ClientRiskAssessments_ClientRi~` FOREIGN KEY (`ClientRiskAssessmentId`) REFERENCES `ClientRiskAssessments` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_ClientRiskAssessmentResponses_RiskFactorDefinitions_RiskFact~` FOREIGN KEY (`RiskFactorDefinitionId`) REFERENCES `RiskFactorDefinitions` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_ClientRiskAssessmentResponses_RiskFactorOptions_RiskFactorOp~` FOREIGN KEY (`RiskFactorOptionId`) REFERENCES `RiskFactorOptions` (`Id`) ON DELETE RESTRICT
);

CREATE UNIQUE INDEX `IX_ClientRiskAssessmentApprovals_ClientRiskAssessmentId_Approver` ON `ClientRiskAssessmentApprovals` (`ClientRiskAssessmentId`, `Approver`);

CREATE INDEX `IX_ClientRiskAssessmentResponses_ClientEvidenceItemId` ON `ClientRiskAssessmentResponses` (`ClientEvidenceItemId`);

CREATE UNIQUE INDEX `IX_ClientRiskAssessmentResponses_ClientRiskAssessmentId_RiskFac~` ON `ClientRiskAssessmentResponses` (`ClientRiskAssessmentId`, `RiskFactorDefinitionId`);

CREATE INDEX `IX_ClientRiskAssessmentResponses_RiskFactorDefinitionId` ON `ClientRiskAssessmentResponses` (`RiskFactorDefinitionId`);

CREATE INDEX `IX_ClientRiskAssessmentResponses_RiskFactorOptionId` ON `ClientRiskAssessmentResponses` (`RiskFactorOptionId`);

CREATE INDEX `IX_ClientRiskAssessments_ClientId_Status` ON `ClientRiskAssessments` (`ClientId`, `Status`);

CREATE INDEX `IX_ClientRiskAssessments_FinalRating_Status` ON `ClientRiskAssessments` (`FinalRating`, `Status`);

CREATE INDEX `IX_ClientRiskAssessments_NextReviewDate` ON `ClientRiskAssessments` (`NextReviewDate`);

CREATE INDEX `IX_ClientRiskAssessments_RiskMethodologyVersionId` ON `ClientRiskAssessments` (`RiskMethodologyVersionId`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260726131207_AddProportionalClientRisk', '10.0.10');

ALTER TABLE `ClientRiskAssessments` ADD `PreviousAssessmentId` int NULL;

ALTER TABLE `ClientRiskAssessments` ADD `ReviewTriggerReason` longtext NULL;

ALTER TABLE `ClientRiskAssessments` ADD `ReviewTriggerType` varchar(48) NOT NULL DEFAULT 'Initial';

ALTER TABLE `ClientRiskAssessments` ADD `ReviewTriggeredAtUtc` datetime(6) NULL;

ALTER TABLE `ClientRiskAssessments` ADD `ReviewTriggeredBy` varchar(191) NULL;

ALTER TABLE `ClientRiskAssessmentResponses` ADD `ConfirmedAtUtc` datetime(6) NULL;

ALTER TABLE `ClientRiskAssessmentResponses` ADD `ConfirmedBy` varchar(191) NULL;

CREATE INDEX `IX_ClientRiskAssessments_PreviousAssessmentId` ON `ClientRiskAssessments` (`PreviousAssessmentId`);

ALTER TABLE `ClientRiskAssessments` ADD CONSTRAINT `FK_ClientRiskAssessments_ClientRiskAssessments_PreviousAssessme~` FOREIGN KEY (`PreviousAssessmentId`) REFERENCES `ClientRiskAssessments` (`Id`) ON DELETE RESTRICT;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260726140344_AddClientRiskReviewCompletion', '10.0.10');

CREATE TABLE `BusinessRiskAssessments` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Name` varchar(191) NOT NULL,
    `AssessmentYear` int NOT NULL,
    `AsAtDate` date NOT NULL,
    `Status` varchar(32) NOT NULL,
    `Scope` longtext NOT NULL,
    `MethodologyNarrative` longtext NOT NULL,
    `ManagementJudgement` longtext NOT NULL,
    `Limitations` longtext NOT NULL,
    `RiskTolerance` longtext NOT NULL,
    `PortfolioSnapshotJson` longtext NULL,
    `SnapshotJson` longtext NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    `SubmittedAtUtc` datetime(6) NULL,
    `ApprovedAtUtc` datetime(6) NULL,
    `ActivatedAtUtc` datetime(6) NULL,
    `PreparedBy` varchar(191) NULL,
    `UpdatedBy` varchar(191) NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `BusinessRiskApprovals` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `BusinessRiskAssessmentId` int NOT NULL,
    `Approver` varchar(191) NOT NULL,
    `Reason` longtext NOT NULL,
    `ApprovedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_BusinessRiskApprovals_BusinessRiskAssessments_BusinessRiskAs~` FOREIGN KEY (`BusinessRiskAssessmentId`) REFERENCES `BusinessRiskAssessments` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `BusinessRiskItems` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `BusinessRiskAssessmentId` int NOT NULL,
    `Category` varchar(48) NOT NULL,
    `RiskStatement` longtext NOT NULL,
    `EvidenceAndRationale` longtext NOT NULL,
    `Likelihood` int NOT NULL,
    `Impact` int NOT NULL,
    `InherentScore` int NOT NULL,
    `InherentRating` varchar(32) NOT NULL,
    `KeyControls` longtext NOT NULL,
    `ControlEffectiveness` varchar(32) NOT NULL,
    `ResidualRating` varchar(32) NOT NULL,
    `ResidualRationale` longtext NOT NULL,
    `TreatmentDecision` varchar(32) NOT NULL,
    `Owner` varchar(191) NOT NULL,
    `DueDate` date NULL,
    `SortOrder` int NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_BusinessRiskItems_BusinessRiskAssessments_BusinessRiskAssess~` FOREIGN KEY (`BusinessRiskAssessmentId`) REFERENCES `BusinessRiskAssessments` (`Id`) ON DELETE CASCADE
);

CREATE UNIQUE INDEX `IX_BusinessRiskApprovals_BusinessRiskAssessmentId_Approver` ON `BusinessRiskApprovals` (`BusinessRiskAssessmentId`, `Approver`);

CREATE INDEX `IX_BusinessRiskAssessments_AssessmentYear_Status` ON `BusinessRiskAssessments` (`AssessmentYear`, `Status`);

CREATE INDEX `IX_BusinessRiskItems_BusinessRiskAssessmentId_Category` ON `BusinessRiskItems` (`BusinessRiskAssessmentId`, `Category`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260726141855_AddBusinessRiskAssessmentFoundation', '10.0.10');

CREATE TABLE `RmcpVersions` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `BusinessRiskAssessmentId` int NOT NULL,
    `Title` varchar(191) NOT NULL,
    `VersionReference` varchar(64) NOT NULL,
    `Status` varchar(32) NOT NULL,
    `Scope` longtext NOT NULL,
    `Owner` varchar(191) NOT NULL,
    `ReviewMonths` int NOT NULL,
    `EffectiveDate` date NULL,
    `NextReviewDate` date NULL,
    `SignedDocumentLocation` varchar(1024) NOT NULL,
    `ApprovalResolutionLocation` varchar(1024) NOT NULL,
    `ChangeSummary` longtext NOT NULL,
    `SnapshotJson` longtext NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    `SubmittedAtUtc` datetime(6) NULL,
    `ApprovedAtUtc` datetime(6) NULL,
    `ActivatedAtUtc` datetime(6) NULL,
    `PreparedBy` varchar(191) NULL,
    `UpdatedBy` varchar(191) NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_RmcpVersions_BusinessRiskAssessments_BusinessRiskAssessmentId` FOREIGN KEY (`BusinessRiskAssessmentId`) REFERENCES `BusinessRiskAssessments` (`Id`) ON DELETE RESTRICT
);

CREATE TABLE `RmcpControls` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `RmcpVersionId` int NOT NULL,
    `BusinessRiskItemId` int NULL,
    `Domain` varchar(48) NOT NULL,
    `Code` varchar(64) NOT NULL,
    `Title` varchar(191) NOT NULL,
    `ProcedureSummary` longtext NOT NULL,
    `Owner` varchar(191) NOT NULL,
    `Frequency` varchar(64) NOT NULL,
    `EvidenceExpectation` longtext NOT NULL,
    `MonitoringMethod` longtext NOT NULL,
    `EscalationProcedure` longtext NOT NULL,
    `HasGap` tinyint(1) NOT NULL,
    `GapDescription` longtext NULL,
    `TreatmentOwner` varchar(191) NULL,
    `TreatmentDueDate` date NULL,
    `ComplianceTaskId` int NULL,
    `SortOrder` int NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_RmcpControls_BusinessRiskItems_BusinessRiskItemId` FOREIGN KEY (`BusinessRiskItemId`) REFERENCES `BusinessRiskItems` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_RmcpControls_ComplianceTasks_ComplianceTaskId` FOREIGN KEY (`ComplianceTaskId`) REFERENCES `ComplianceTasks` (`Id`) ON DELETE SET NULL,
    CONSTRAINT `FK_RmcpControls_RmcpVersions_RmcpVersionId` FOREIGN KEY (`RmcpVersionId`) REFERENCES `RmcpVersions` (`Id`) ON DELETE CASCADE
);

CREATE INDEX `IX_RmcpControls_BusinessRiskItemId` ON `RmcpControls` (`BusinessRiskItemId`);

CREATE INDEX `IX_RmcpControls_ComplianceTaskId` ON `RmcpControls` (`ComplianceTaskId`);

CREATE UNIQUE INDEX `IX_RmcpControls_RmcpVersionId_Code` ON `RmcpControls` (`RmcpVersionId`, `Code`);

CREATE INDEX `IX_RmcpVersions_BusinessRiskAssessmentId` ON `RmcpVersions` (`BusinessRiskAssessmentId`);

CREATE INDEX `IX_RmcpVersions_Status_VersionReference` ON `RmcpVersions` (`Status`, `VersionReference`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260726164754_AddRmcpControlFoundation', '10.0.10');

ALTER TABLE `ComplianceTasks` ADD `BusinessRiskAssessmentId` int NULL;

ALTER TABLE `ComplianceTasks` ADD `ClientId` int NULL;

ALTER TABLE `ComplianceTasks` ADD `ClientRiskAssessmentId` int NULL;

ALTER TABLE `ComplianceTasks` ADD `ClosedBy` varchar(191) NULL;

ALTER TABLE `ComplianceTasks` ADD `ClosureReason` longtext NULL;

ALTER TABLE `ComplianceTasks` ADD `ClosureRequestedAtUtc` datetime(6) NULL;

ALTER TABLE `ComplianceTasks` ADD `ClosureRequestedBy` varchar(191) NULL;

ALTER TABLE `ComplianceTasks` ADD `EscalatedAtUtc` datetime(6) NULL;

ALTER TABLE `ComplianceTasks` ADD `EscalatedBy` varchar(191) NULL;

ALTER TABLE `ComplianceTasks` ADD `EvidenceSummary` longtext NULL;

ALTER TABLE `ComplianceTasks` ADD `Outcome` longtext NULL;

ALTER TABLE `ComplianceTasks` ADD `RmcpControlId` int NULL;

ALTER TABLE `ComplianceTasks` ADD `RmcpVersionId` int NULL;

ALTER TABLE `ComplianceTasks` ADD `TaskType` varchar(48) NOT NULL DEFAULT 'Remediation';

CREATE INDEX `IX_ComplianceTasks_BusinessRiskAssessmentId` ON `ComplianceTasks` (`BusinessRiskAssessmentId`);

CREATE INDEX `IX_ComplianceTasks_ClientId` ON `ComplianceTasks` (`ClientId`);

CREATE INDEX `IX_ComplianceTasks_ClientRiskAssessmentId` ON `ComplianceTasks` (`ClientRiskAssessmentId`);

CREATE INDEX `IX_ComplianceTasks_RmcpControlId` ON `ComplianceTasks` (`RmcpControlId`);

CREATE INDEX `IX_ComplianceTasks_RmcpVersionId` ON `ComplianceTasks` (`RmcpVersionId`);

CREATE INDEX `IX_ComplianceTasks_TaskType_Status_DueDate` ON `ComplianceTasks` (`TaskType`, `Status`, `DueDate`);

ALTER TABLE `ComplianceTasks` ADD CONSTRAINT `FK_ComplianceTasks_BusinessRiskAssessments_BusinessRiskAssessme~` FOREIGN KEY (`BusinessRiskAssessmentId`) REFERENCES `BusinessRiskAssessments` (`Id`) ON DELETE RESTRICT;

ALTER TABLE `ComplianceTasks` ADD CONSTRAINT `FK_ComplianceTasks_ClientRiskAssessments_ClientRiskAssessmentId` FOREIGN KEY (`ClientRiskAssessmentId`) REFERENCES `ClientRiskAssessments` (`Id`) ON DELETE RESTRICT;

ALTER TABLE `ComplianceTasks` ADD CONSTRAINT `FK_ComplianceTasks_Clients_ClientId` FOREIGN KEY (`ClientId`) REFERENCES `Clients` (`Id`) ON DELETE RESTRICT;

ALTER TABLE `ComplianceTasks` ADD CONSTRAINT `FK_ComplianceTasks_RmcpControls_RmcpControlId` FOREIGN KEY (`RmcpControlId`) REFERENCES `RmcpControls` (`Id`) ON DELETE RESTRICT;

ALTER TABLE `ComplianceTasks` ADD CONSTRAINT `FK_ComplianceTasks_RmcpVersions_RmcpVersionId` FOREIGN KEY (`RmcpVersionId`) REFERENCES `RmcpVersions` (`Id`) ON DELETE RESTRICT;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260726170142_AddComplianceWorkRegister', '10.0.10');

CREATE TABLE `InspectionCases` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Reference` varchar(96) NOT NULL,
    `Title` varchar(240) NOT NULL,
    `RequestingAuthority` varchar(191) NOT NULL,
    `AsAtDate` date NOT NULL,
    `RequestDate` date NOT NULL,
    `DueDate` date NOT NULL,
    `Status` varchar(32) NOT NULL,
    `Scope` longtext NOT NULL,
    `Coordinator` varchar(191) NOT NULL,
    `Notes` longtext NULL,
    `SnapshotJson` longtext NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    `FrozenAtUtc` datetime(6) NULL,
    `CreatedBy` varchar(191) NULL,
    `UpdatedBy` varchar(191) NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE `InspectionReadinessChecks` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `InspectionCaseId` int NOT NULL,
    `CheckType` varchar(64) NOT NULL,
    `Status` varchar(32) NOT NULL,
    `EvidenceLocation` longtext NULL,
    `Notes` longtext NULL,
    `TestedAtUtc` datetime(6) NULL,
    `TestedBy` varchar(191) NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_InspectionReadinessChecks_InspectionCases_InspectionCaseId` FOREIGN KEY (`InspectionCaseId`) REFERENCES `InspectionCases` (`Id`) ON DELETE CASCADE
);

CREATE TABLE `InspectionRequestItems` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `InspectionCaseId` int NOT NULL,
    `Category` varchar(64) NOT NULL,
    `Title` varchar(240) NOT NULL,
    `Description` longtext NULL,
    `Owner` varchar(191) NOT NULL,
    `DueDate` date NOT NULL,
    `Status` varchar(32) NOT NULL,
    `EvidenceTitle` longtext NULL,
    `EvidenceLocation` longtext NULL,
    `LinkedEntityType` varchar(128) NULL,
    `LinkedEntityId` int NULL,
    `ReviewNotes` longtext NULL,
    `CompletedAtUtc` datetime(6) NULL,
    `CompletedBy` varchar(191) NULL,
    `SortOrder` int NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_InspectionRequestItems_InspectionCases_InspectionCaseId` FOREIGN KEY (`InspectionCaseId`) REFERENCES `InspectionCases` (`Id`) ON DELETE CASCADE
);

CREATE INDEX `IX_InspectionCases_Status_DueDate` ON `InspectionCases` (`Status`, `DueDate`);

CREATE UNIQUE INDEX `IX_InspectionReadinessChecks_InspectionCaseId_CheckType` ON `InspectionReadinessChecks` (`InspectionCaseId`, `CheckType`);

CREATE INDEX `IX_InspectionRequestItems_InspectionCaseId_Status_DueDate` ON `InspectionRequestItems` (`InspectionCaseId`, `Status`, `DueDate`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260726173018_AddInspectionReadiness', '10.0.10');

ALTER TABLE `Clients` ADD `DuplicateOfClientId` int NULL;

ALTER TABLE `Clients` ADD `LifecycleReason` varchar(1000) NULL;

ALTER TABLE `Clients` ADD `LifecycleReviewedAtUtc` datetime(6) NULL;

ALTER TABLE `Clients` ADD `LifecycleReviewedBy` varchar(191) NULL;

ALTER TABLE `Clients` ADD `LifecycleStatus` varchar(32) NOT NULL DEFAULT 'Unreviewed';

CREATE TABLE `ClientVerificationItems` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClientId` int NOT NULL,
    `FieldCode` varchar(64) NOT NULL,
    `FieldLabel` varchar(191) NOT NULL,
    `ChangeType` varchar(32) NOT NULL,
    `ExistingValue` longtext NULL,
    `ProposedValue` longtext NULL,
    `SourceReference` varchar(1024) NOT NULL,
    `Recommendation` longtext NULL,
    `Status` varchar(32) NOT NULL,
    `IsBlocking` tinyint(1) NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `CreatedBy` varchar(191) NOT NULL,
    `DecidedAtUtc` datetime(6) NULL,
    `DecidedBy` varchar(191) NULL,
    `DecisionReason` varchar(1000) NULL,
    `AppliedAtUtc` datetime(6) NULL,
    `AppliedBy` varchar(191) NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientVerificationItems_Clients_ClientId` FOREIGN KEY (`ClientId`) REFERENCES `Clients` (`Id`) ON DELETE CASCADE
);

CREATE INDEX `IX_Clients_DuplicateOfClientId` ON `Clients` (`DuplicateOfClientId`);

CREATE INDEX `IX_Clients_LifecycleStatus` ON `Clients` (`LifecycleStatus`);

CREATE INDEX `IX_ClientVerificationItems_ClientId_Status_IsBlocking` ON `ClientVerificationItems` (`ClientId`, `Status`, `IsBlocking`);

ALTER TABLE `Clients` ADD CONSTRAINT `FK_Clients_Clients_DuplicateOfClientId` FOREIGN KEY (`DuplicateOfClientId`) REFERENCES `Clients` (`Id`) ON DELETE RESTRICT;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260726175421_AddClientOperationalVerification', '10.0.10');

CREATE TABLE `ClientReviewTransferRecords` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `PackageId` varchar(36) NOT NULL,
    `Direction` varchar(16) NOT NULL,
    `ContentSha256` varchar(64) NOT NULL,
    `ClientId` int NOT NULL,
    `Status` varchar(32) NOT NULL,
    `FileName` varchar(260) NOT NULL,
    `StoragePath` varchar(512) NOT NULL,
    `SummaryJson` longtext NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `AppliedAtUtc` datetime(6) NULL,
    `AppliedBy` varchar(191) NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ClientReviewTransferRecords_Clients_ClientId` FOREIGN KEY (`ClientId`) REFERENCES `Clients` (`Id`) ON DELETE RESTRICT
);

CREATE INDEX `IX_ClientReviewTransferRecords_ClientId_CreatedAtUtc` ON `ClientReviewTransferRecords` (`ClientId`, `CreatedAtUtc`);

CREATE INDEX `IX_ClientReviewTransferRecords_Direction_ContentSha256` ON `ClientReviewTransferRecords` (`Direction`, `ContentSha256`);

CREATE UNIQUE INDEX `IX_ClientReviewTransferRecords_Direction_PackageId` ON `ClientReviewTransferRecords` (`Direction`, `PackageId`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260727083241_AddClientReviewTransfers', '10.0.10');

COMMIT;

