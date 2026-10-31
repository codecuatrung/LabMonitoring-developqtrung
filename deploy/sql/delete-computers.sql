/*
  Bulk-delete registered computers (and everything that hangs off them) so the agents can register again.

  Deletes, for every targeted computer:
    - CommandResults, SessionComputers            (FK without cascade -> removed explicitly first)
    - Commands addressed to that single computer  (room-wide commands are kept; only this computer's results go)
    - Computers                                    (cascades ComputerMetrics, ComputerSpecs, ComputerStatuses, Screenshots)
  Kept: rooms, buildings, classes, sessions, users/roles/permissions, policies, audit logs, agent packages.

  HOW TO USE
    1. Set @RoomCode (NULL = every room) below.
    2. Run once as is: @Execute = 0 is a DRY RUN - prints what would be deleted, then rolls back.
    3. Set @Execute = 1 and run again to really delete.
  Back up the database first (BACKUP DATABASE ...) - deletion cannot be undone.

  After deleting, each agent must forget its old token: on every lab PC (elevated PowerShell)
    Stop-Service LabMonitoringAgent
    Remove-Item C:\ProgramData\LabMonitoring\Agent\credentials.dat -Force
    Start-Service LabMonitoringAgent
  then approve the newly registered machines in the web admin.
*/
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;   -- required by the filtered indexes on these tables
SET XACT_ABORT ON;

DECLARE @RoomCode nvarchar(50) = NULL;   -- e.g. N'LAB-01'; NULL = all rooms
DECLARE @Execute  bit          = 0;      -- 0 = dry run (rollback), 1 = really delete

DECLARE @Targets TABLE (Id int PRIMARY KEY, Name nvarchar(200), RoomCode nvarchar(50));

INSERT INTO @Targets (Id, Name, RoomCode)
SELECT c.Id, c.Name, r.Code
FROM dbo.Computers c
JOIN dbo.Rooms r ON r.Id = c.RoomId
WHERE @RoomCode IS NULL OR r.Code = @RoomCode;

IF NOT EXISTS (SELECT 1 FROM @Targets)
BEGIN
    PRINT 'No computers match - nothing to do.';
    RETURN;
END

DECLARE @total int = (SELECT COUNT(*) FROM @Targets);
PRINT CONCAT('Computers targeted: ', @total,
             CASE WHEN @RoomCode IS NULL THEN ' (all rooms)' ELSE CONCAT(' (room ', @RoomCode, ')') END);
SELECT RoomCode, Id, Name FROM @Targets ORDER BY RoomCode, Name;

BEGIN TRANSACTION;

DECLARE @n int;

DELETE cr FROM dbo.CommandResults cr WHERE cr.ComputerId IN (SELECT Id FROM @Targets);
SET @n = @@ROWCOUNT; PRINT CONCAT('CommandResults deleted:    ', @n);

-- Commands sent to exactly one machine (ComputerId set). Their results are already gone; room-wide
-- commands (ComputerId NULL) are untouched.
DELETE cm FROM dbo.Commands cm WHERE cm.ComputerId IN (SELECT Id FROM @Targets);
SET @n = @@ROWCOUNT; PRINT CONCAT('Commands deleted:          ', @n);

DELETE sc FROM dbo.SessionComputers sc WHERE sc.ComputerId IN (SELECT Id FROM @Targets);
SET @n = @@ROWCOUNT; PRINT CONCAT('SessionComputers deleted:  ', @n);

DELETE c FROM dbo.Computers c WHERE c.Id IN (SELECT Id FROM @Targets);
SET @n = @@ROWCOUNT; PRINT CONCAT('Computers deleted:         ', @n, '  (+ metrics/specs/status/screenshots via cascade)');

IF @Execute = 1
BEGIN
    COMMIT TRANSACTION;
    PRINT 'COMMITTED. Now clear credentials.dat on each lab PC and restart the agent service.';
END
ELSE
BEGIN
    ROLLBACK TRANSACTION;
    PRINT 'DRY RUN - rolled back, nothing was deleted. Set @Execute = 1 to delete for real.';
END
