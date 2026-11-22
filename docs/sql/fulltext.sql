-- OPTIONAL: SQL Server Full-Text Search for the catalogue.
--
-- NOT wired into the app and NOT tested: the SQL Server Express used for development has no Full-Text feature installed
-- (FULLTEXTSERVICEPROPERTY('IsFullTextInstalled') = 0), so this script could not be run here. The app searches with
-- LIKE '%term%' today (docs/adr/0016-search-like-not-fulltext.md). Use this when the catalogue outgrows that, on a server that
-- has the feature (Azure SQL Database, SQL Server Standard/Developer with Full-Text installed).
--
-- 1. Check the feature:  SELECT FULLTEXTSERVICEPROPERTY('IsFullTextInstalled');   -- must return 1
-- 2. Run the statements below once, outside a transaction.
-- 3. Then change EfGameRepository.SearchAsync to use  EF.Functions.Contains(...)  or  EF.Functions.FreeText(...)  on
--    Title and Description instead of EF.Functions.Like, and measure it with: dotnet run --project benchmarks/GameVault.Benchmarks -- queries
--
-- Index population is asynchronous: right after creation, CONTAINS can return fewer rows than exist. Do not run a test that
-- searches immediately after creating the index without waiting for population to finish:
--   SELECT FULLTEXTCATALOGPROPERTY('GameVaultCatalog', 'PopulateStatus');   -- 0 = idle (finished)

CREATE FULLTEXT CATALOG GameVaultCatalog AS DEFAULT;
GO

-- KEY INDEX must be a unique, single-column, non-nullable index: the primary key of Games.
DECLARE @pk sysname = (SELECT name FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID('dbo.Games') AND type = 'PK');
DECLARE @sql nvarchar(max) = N'CREATE FULLTEXT INDEX ON dbo.Games (Title LANGUAGE 1033, Description LANGUAGE 1033) KEY INDEX ' + QUOTENAME(@pk)
                           + N' ON GameVaultCatalog WITH CHANGE_TRACKING AUTO;';
EXEC sp_executesql @sql;
GO

-- Example queries
-- All words must appear (prefix match on the last word, as a visitor typing would expect):
--   SELECT TOP 12 Id, Title FROM dbo.Games WHERE CONTAINS((Title, Description), '"dragon*" AND "quest*"');
-- Natural language, ranked:
--   SELECT TOP 12 g.Id, g.Title, k.RANK FROM dbo.Games g JOIN FREETEXTTABLE(dbo.Games, (Title, Description), 'dragon quest') k ON g.Id = k.[KEY] ORDER BY k.RANK DESC;
