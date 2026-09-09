USE SingleBuyerLssRepository;
GO

SELECT
    DB_NAME() AS DatabaseName,
    @@SERVERNAME AS ServerName;
GO

SELECT
    users.UserName,
    roles.Name AS RoleName,
    CONCAT(LEFT(users.PasswordHash, 18), '...') AS PasswordHashPreview
FROM dbo.AspNetUsers AS users
INNER JOIN dbo.AspNetUserRoles AS userRoles ON users.Id = userRoles.UserId
INNER JOIN dbo.AspNetRoles AS roles ON userRoles.RoleId = roles.Id
ORDER BY users.UserName;
GO

SELECT
    ReferenceNumber,
    Title,
    Category,
    ProjectStage,
    UpdatedAt
FROM dbo.SolarDocuments
ORDER BY UpdatedAt DESC;
GO
