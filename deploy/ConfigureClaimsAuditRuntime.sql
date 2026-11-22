-- Run in the claims database as a migration administrator.
-- Replace NULL with the name of the existing database user used by the API.
DECLARE @ApplicationUser sysname = NULL;
IF @ApplicationUser IS NULL OR DATABASE_PRINCIPAL_ID(@ApplicationUser) IS NULL
    THROW 50003, 'Set ApplicationUser to an existing claims API database user.', 1;

IF @ApplicationUser = N'dbo' OR IS_ROLEMEMBER(N'db_owner', @ApplicationUser) = 1
    THROW 50004, 'Use a claims API database user without ownership privileges.', 1;

IF DATABASE_PRINCIPAL_ID(N'ClaimsAuditWriter') IS NULL
    THROW 50005, 'Apply the claims migrations before enrolling the API database user.', 1;

IF NOT EXISTS (
    SELECT 1
    FROM sys.database_role_members
    WHERE role_principal_id = DATABASE_PRINCIPAL_ID(N'ClaimsAuditWriter')
      AND member_principal_id = DATABASE_PRINCIPAL_ID(@ApplicationUser)
)
BEGIN
    DECLARE @Enroll nvarchar(400) = N'ALTER ROLE [ClaimsAuditWriter] ADD MEMBER ' + QUOTENAME(@ApplicationUser);
    EXEC sys.sp_executesql @Enroll;
END;
