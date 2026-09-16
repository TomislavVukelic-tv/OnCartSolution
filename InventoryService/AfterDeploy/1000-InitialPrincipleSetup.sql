INSERT INTO
    Common.Role (ID, Name)
SELECT
    NEWID(), wanted.Name
FROM
    (
        VALUES ('admin'), ('customer')
    ) AS wanted(Name)
WHERE
    NOT EXISTS (
        SELECT
            1
        FROM
            Common.Role existing
        WHERE
            existing.Name = wanted.Name
    );

INSERT INTO
    Common.RolePermission (ID, RoleID, ClaimID, IsAuthorized)
SELECT
    NEWID(),
    r.ID,
    c.ID,
    1
FROM
    Common.Role r
    CROSS JOIN Common.Claim c
WHERE
    r.Name = 'admin'
    AND c.ClaimResource LIKE 'Inventory.%'
    AND c.Active = 1
    AND NOT EXISTS (
        SELECT
            1
        FROM
            Common.RolePermission rp
        WHERE
            rp.RoleID = r.ID
            AND rp.ClaimID = c.ID
    );

INSERT INTO
    Common.Principal (ID, Name)
SELECT
    NEWID(),
    wanted.Name
FROM
    (
        VALUES
            ('admin'),
            ('user'),
            ('guest')
    ) AS wanted(Name)
WHERE
    NOT EXISTS (
        SELECT
            1
        FROM
            Common.Principal existing
        WHERE
            existing.Name = wanted.Name
    );


INSERT INTO
    Common.PrincipalHasRole (ID, PrincipalID, RoleID)
SELECT
    NEWID(),
    p.ID,
    r.ID
FROM
    (
        VALUES
            ('admin', 'admin'),
            ('user', 'customer'),
            ('guest', 'customer')
    ) AS map(PrincipalName, RoleName)
    JOIN Common.Principal p ON p.Name = map.PrincipalName
    JOIN Common.Role r ON r.Name = map.RoleName
WHERE
    NOT EXISTS (
        SELECT
            1
        FROM
            Common.PrincipalHasRole phr
        WHERE
            phr.PrincipalID = p.ID
            AND phr.RoleID = r.ID
    );
