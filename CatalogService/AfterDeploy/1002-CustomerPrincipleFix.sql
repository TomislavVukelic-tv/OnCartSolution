DELETE phr
FROM
    Common.PrincipalHasRole phr
    JOIN Common.Principal p ON p.ID = phr.PrincipalID
WHERE
    p.Name = 'customer';

DELETE FROM
    Common.Principal
WHERE
    Name = 'customer';

INSERT INTO
    Common.Principal (ID, Name)
SELECT
    NEWID(),
    wanted.Name
FROM
    (
        VALUES
            ('admin'),
            ('user')
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
            ('user', 'customer')
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