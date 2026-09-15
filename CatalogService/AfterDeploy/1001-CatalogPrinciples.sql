INSERT INTO
    Common.Principal (ID, Name)
SELECT
    NEWID(),
    wanted.Name
FROM
    (
        VALUES
            ('admin'),
            ('customer')
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
    JOIN Common.Role r ON r.Name = p.Name
WHERE
    p.Name IN ('admin', 'customer')
    AND NOT EXISTS (
        SELECT
            1
        FROM
            Common.PrincipalHasRole phr
        WHERE
            phr.PrincipalID = p.ID
            AND phr.RoleID = r.ID
    );