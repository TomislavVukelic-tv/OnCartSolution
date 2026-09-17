INSERT INTO
    Common.Role (ID, Name)
SELECT
    NEWID(), wanted.Name
FROM
    (
        VALUES ('event-publisher')
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
    r.Name = 'event-publisher'
    AND c.ClaimResource = 'Inventory.StockItem'
    AND c.ClaimRight IN ('New', 'Edit')
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
        VALUES ('catalog-publisher')
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
    Common.Principal p
    JOIN Common.Role r ON r.Name = 'event-publisher'
WHERE
    p.Name = 'catalog-publisher'
    AND NOT EXISTS (
        SELECT
            1
        FROM
            Common.PrincipalHasRole phr
        WHERE
            phr.PrincipalID = p.ID
            AND phr.RoleID = r.ID
    );
