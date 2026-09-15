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

-- 2) admin -> FULL access (Read/New/Edit/Remove) on every Catalog.* claim.
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
    AND c.ClaimResource LIKE 'Catalog.%'
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

-- 3) customer -> READ-ONLY on every Catalog.* claim.
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
    r.Name = 'customer'
    AND c.ClaimResource LIKE 'Catalog.%'
    AND c.ClaimRight = 'Read'
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