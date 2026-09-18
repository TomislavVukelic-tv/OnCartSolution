INSERT INTO
    Common.Role (ID, Name)
SELECT
    NEWID(), wanted.Name
FROM
    (
        VALUES ('admin'), ('customer'), ('event-publisher')
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
    AND c.ClaimResource LIKE 'Cart.%'
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
    AND c.ClaimResource = 'Cart.AddToCart'
    AND c.ClaimRight = 'Execute'
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
    AND c.ClaimResource IN ('Cart.ShoppingCart', 'Cart.CartItem')
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
    AND c.ClaimResource = 'Cart.ProductSnapshot'
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
        VALUES
            ('admin'),
            ('customer'),
            ('catalog-publisher')
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
            ('customer', 'customer'),
            ('catalog-publisher', 'event-publisher')
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
