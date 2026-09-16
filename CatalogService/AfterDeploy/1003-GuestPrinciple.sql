INSERT INTO
    Common.Principal (ID, Name)
SELECT
    NEWID(),
    'guest'
WHERE
    NOT EXISTS (
        SELECT
            1
        FROM
            Common.Principal existing
        WHERE
            existing.Name = 'guest'
    );

INSERT INTO
    Common.PrincipalHasRole (ID, PrincipalID, RoleID)
SELECT
    NEWID(),
    p.ID,
    r.ID
FROM
    Common.Principal p
    JOIN Common.Role r ON r.Name = 'customer'
WHERE
    p.Name = 'guest'
    AND NOT EXISTS (
        SELECT
            1
        FROM
            Common.PrincipalHasRole phr
        WHERE
            phr.PrincipalID = p.ID
            AND phr.RoleID = r.ID
    );