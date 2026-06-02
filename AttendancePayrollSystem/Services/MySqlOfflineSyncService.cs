using System;
using System.Collections.Generic;
using AttendancePayrollSystem.DataAccess;
using AttendancePayrollSystem.Models;
using MySqlConnector;

namespace AttendancePayrollSystem.Services
{
    public static class MySqlOfflineSyncService
    {
        private const string SyncQueueTableName = "SyncQueue";
        private const long OfflineIdentityFloor = 1000000000;
        private const string AutoSyncEnvVar = "ATTENDANCE_OFFLINE_AUTO_SYNC";
        private const string SyncIntervalSecondsEnvVar = "ATTENDANCE_OFFLINE_SYNC_INTERVAL_SECONDS";
        private static readonly object _syncGate = new();
        private static bool _syncInProgress;
        private static DateTime _lastOfflineMirrorRefreshUtc = DateTime.MinValue;
        private static string OfflineStoreLabel => $"local {GetOfflineStoreLabel()}";

        public static bool IsEnabled =>
            !SupabaseConfig.UseApi && DatabaseHelper.IsOfflineConfigured();

        public static bool IsAutoSyncEnabled =>
            GetOptionalBool(AutoSyncEnvVar, defaultValue: true);

        public static TimeSpan SyncInterval =>
            TimeSpan.FromSeconds(Math.Max(10, GetOptionalInt(SyncIntervalSecondsEnvVar, defaultValue: 15)));

        public static OfflineSyncInitializationResult InitializeRuntime()
        {
            if (SupabaseConfig.UseApi)
            {
                DatabaseRuntimeState.SetPreferredMode(false);
                DatabaseRuntimeState.SetRuntimeState(
                    useOfflineDatabase: false,
                    isOnlineAvailable: false,
                    isOfflineDatabaseAvailable: false,
                    statusMessage: "Supabase API mode is active. Local MySQL offline sync is disabled.");

                return new OfflineSyncInitializationResult(true, false, 0, DatabaseRuntimeState.StatusMessage);
            }

            var offlineAvailable = EnsureOfflineDatabaseReady(out var offlineMessage);
            var onlineAvailable = DatabaseHelper.CanConnect(DatabaseConnectionTarget.Online, out var onlineMessage);
            var preferOffline = offlineAvailable && DatabaseRuntimeState.PreferOfflineDatabase;

            if (onlineAvailable)
            {
                try
                {
                    var onlineDetails = EnsureOnlineDatabaseReady();
                    var pendingCount = offlineAvailable ? GetPendingOperationCount() : 0;

                    if (offlineAvailable)
                    {
                        if (pendingCount > 0)
                        {
                            SynchronizePendingChanges();
                        }

                        RefreshOfflineMirrorFromOnline();
                    }

                    var statusMessage = preferOffline
                        ? $"Offline database selected. Using the {OfflineStoreLabel} while the online database remains available for synchronization.\nOnline target: {DatabaseHelper.GetConnectionSummary(DatabaseConnectionTarget.Online)}"
                        : offlineAvailable
                            ? $"Online database ready. {OfflineStoreLabel} is synchronized. {DatabaseHelper.GetConnectionSummary(DatabaseConnectionTarget.Online)}"
                            : $"Online database ready. {DatabaseHelper.GetConnectionSummary(DatabaseConnectionTarget.Online)}";
                    if (!preferOffline && offlineAvailable)
                    {
                        statusMessage = $"{statusMessage}\nOffline target: {DatabaseHelper.GetConnectionSummary(DatabaseConnectionTarget.Offline)}";
                    }

                    if (!string.IsNullOrWhiteSpace(onlineDetails))
                    {
                        statusMessage = $"{statusMessage}\n{onlineDetails}";
                    }

                    if (offlineAvailable)
                    {
                        statusMessage = $"{statusMessage}\nPending sync operations: {GetPendingOperationCount()}";
                    }

                    DatabaseRuntimeState.SetRuntimeState(
                        useOfflineDatabase: preferOffline,
                        isOnlineAvailable: true,
                        isOfflineDatabaseAvailable: offlineAvailable,
                        statusMessage: statusMessage);

                    return new OfflineSyncInitializationResult(true, preferOffline, offlineAvailable ? GetPendingOperationCount() : 0, statusMessage);
                }
                catch (Exception ex)
                {
                    if (offlineAvailable && HasOfflineLoginData())
                    {
                        var warningMessage =
                            $"Online database initialization failed. The app switched to the {OfflineStoreLabel}.\n\n{ex.Message}";
                        DatabaseRuntimeState.SetRuntimeState(
                            useOfflineDatabase: true,
                            isOnlineAvailable: false,
                            isOfflineDatabaseAvailable: true,
                            statusMessage: warningMessage);

                        return new OfflineSyncInitializationResult(true, true, GetPendingOperationCount(), warningMessage);
                    }

                    var errorMessage = $"Failed to initialize the online database.\n\n{ex.Message}";
                    DatabaseRuntimeState.SetRuntimeState(
                        useOfflineDatabase: false,
                        isOnlineAvailable: true,
                        isOfflineDatabaseAvailable: offlineAvailable,
                        statusMessage: errorMessage);

                    return new OfflineSyncInitializationResult(false, false, 0, errorMessage);
                }
            }

            if (offlineAvailable && HasOfflineLoginData())
            {
                var pendingCount = GetPendingOperationCount();
                var statusMessage =
                    $"Offline mode active. Using the {OfflineStoreLabel} because the online database is unavailable.\nLocal target: {offlineMessage}\nPending sync operations: {pendingCount}";

                DatabaseRuntimeState.SetRuntimeState(
                    useOfflineDatabase: true,
                    isOnlineAvailable: false,
                    isOfflineDatabaseAvailable: true,
                    statusMessage: statusMessage);

                return new OfflineSyncInitializationResult(true, true, pendingCount, statusMessage);
            }

            if (offlineAvailable && !HasOfflineLoginData())
            {
                // Offline DB exists but has no login data — seed the bootstrap admin so the user can sign in.
                try
                {
                    var authRepository = new AuthRepository();
                    authRepository.EnsureAuthSchemaAndSeedDefaults(DatabaseConnectionTarget.Offline);

                    if (HasOfflineLoginData())
                    {
                        var pendingCount = GetPendingOperationCount();
                        var statusMessage =
                            $"Offline mode active. Bootstrap admin seeded into the {OfflineStoreLabel}.\nLocal target: {offlineMessage}\nPending sync operations: {pendingCount}";

                        DatabaseRuntimeState.SetRuntimeState(
                            useOfflineDatabase: true,
                            isOnlineAvailable: false,
                            isOfflineDatabaseAvailable: true,
                            statusMessage: statusMessage);

                        return new OfflineSyncInitializationResult(true, true, pendingCount, statusMessage);
                    }
                }
                catch
                {
                    // Fall through to the unavailable message below.
                }
            }

            var unavailableMessage = offlineAvailable
                ? $"Cannot reach the online database, and the {OfflineStoreLabel} does not have cached login data.\nOnline target error: {onlineMessage}"
                : DatabaseHelper.IsOfflineConfigured()
                    ? $"Cannot reach the online database and the {OfflineStoreLabel} is not available.\nOnline target error: {onlineMessage}\nOffline target error: {offlineMessage}"
                    : $"Cannot reach the online database.\n{onlineMessage}\n\nConfigure ATTENDANCE_OFFLINE_DB_CONNECTION for local offline support.";

            DatabaseRuntimeState.SetRuntimeState(
                useOfflineDatabase: false,
                isOnlineAvailable: false,
                isOfflineDatabaseAvailable: offlineAvailable,
                statusMessage: unavailableMessage);

            return new OfflineSyncInitializationResult(false, false, offlineAvailable ? GetPendingOperationCount() : 0, unavailableMessage);
        }

        public static OfflineSyncInitializationResult TrySynchronizeNow()
        {
            if (SupabaseConfig.UseApi)
            {
                return new OfflineSyncInitializationResult(true, false, 0, "Supabase API mode is active.");
            }

            lock (_syncGate)
            {
                if (_syncInProgress)
                {
                    return new OfflineSyncInitializationResult(
                        !DatabaseRuntimeState.UseOfflineDatabase || DatabaseRuntimeState.IsOfflineDatabaseAvailable,
                        DatabaseRuntimeState.UseOfflineDatabase,
                        DatabaseRuntimeState.IsOfflineDatabaseAvailable ? GetPendingOperationCount() : 0,
                        DatabaseRuntimeState.StatusMessage);
                }

                _syncInProgress = true;
            }

            try
            {
                var offlineAvailable = EnsureOfflineDatabaseReady(out var offlineMessage);
                var onlineAvailable = DatabaseHelper.CanConnect(DatabaseConnectionTarget.Online, out var onlineMessage);
                var preferOffline = offlineAvailable && DatabaseRuntimeState.PreferOfflineDatabase;

                if (!onlineAvailable)
                {
                    if (offlineAvailable)
                    {
                        var offlinePendingCount = GetPendingOperationCount();
                        var offlineStatusMessage =
                            $"Offline mode active. Using the {OfflineStoreLabel} because the online database is unavailable.\nLocal target: {offlineMessage}\nPending sync operations: {offlinePendingCount}";

                        DatabaseRuntimeState.SetRuntimeState(
                            useOfflineDatabase: true,
                            isOnlineAvailable: false,
                            isOfflineDatabaseAvailable: true,
                            statusMessage: offlineStatusMessage);

                        return new OfflineSyncInitializationResult(true, true, offlinePendingCount, offlineStatusMessage);
                    }

                    var errorMessage = $"Cannot reach the online database.\n{onlineMessage}";
                    DatabaseRuntimeState.SetRuntimeState(
                        useOfflineDatabase: false,
                        isOnlineAvailable: false,
                        isOfflineDatabaseAvailable: false,
                        statusMessage: errorMessage);

                    return new OfflineSyncInitializationResult(false, false, 0, errorMessage);
                }

                var onlineDetails = EnsureOnlineDatabaseReady();
                var pendingCountBeforeSync = offlineAvailable ? GetPendingOperationCount() : 0;

                if (offlineAvailable && pendingCountBeforeSync > 0)
                {
                    SynchronizePendingChanges();
                }

                if (offlineAvailable &&
                    (pendingCountBeforeSync > 0 || DateTime.UtcNow - _lastOfflineMirrorRefreshUtc >= TimeSpan.FromMinutes(1)))
                {
                    RefreshOfflineMirrorFromOnline();
                }

                var pendingCount = offlineAvailable ? GetPendingOperationCount() : 0;
                var statusMessage = preferOffline
                    ? $"Offline database selected. Using the {OfflineStoreLabel} while the online database remains available for synchronization.\nOnline target: {DatabaseHelper.GetConnectionSummary(DatabaseConnectionTarget.Online)}"
                    : offlineAvailable
                        ? $"Online database ready. {OfflineStoreLabel} is synchronized. {DatabaseHelper.GetConnectionSummary(DatabaseConnectionTarget.Online)}"
                        : $"Online database ready. {DatabaseHelper.GetConnectionSummary(DatabaseConnectionTarget.Online)}";
                if (!preferOffline && offlineAvailable)
                {
                    statusMessage = $"{statusMessage}\nOffline target: {DatabaseHelper.GetConnectionSummary(DatabaseConnectionTarget.Offline)}";
                }

                if (!string.IsNullOrWhiteSpace(onlineDetails))
                {
                    statusMessage = $"{statusMessage}\n{onlineDetails}";
                }

                if (offlineAvailable)
                {
                    statusMessage = $"{statusMessage}\nPending sync operations: {pendingCount}";
                }

                DatabaseRuntimeState.SetRuntimeState(
                    useOfflineDatabase: preferOffline,
                    isOnlineAvailable: true,
                    isOfflineDatabaseAvailable: offlineAvailable,
                    statusMessage: statusMessage);

                return new OfflineSyncInitializationResult(true, preferOffline, pendingCount, statusMessage);
            }
            catch (Exception ex)
            {
                if (DatabaseHelper.CanConnect(DatabaseConnectionTarget.Offline, out var offlineTargetMessage) && HasOfflineLoginData())
                {
                    var fallbackMessage =
                        $"Synchronization warning. The app switched to the {OfflineStoreLabel}.\nLocal target: {offlineTargetMessage}\n\n{ex.Message}";

                    DatabaseRuntimeState.SetRuntimeState(
                        useOfflineDatabase: true,
                        isOnlineAvailable: false,
                        isOfflineDatabaseAvailable: true,
                        statusMessage: fallbackMessage);

                    return new OfflineSyncInitializationResult(true, true, GetPendingOperationCount(), fallbackMessage);
                }

                var errorMessage = $"Failed to synchronize the {OfflineStoreLabel}.\n\n{ex.Message}";
                DatabaseRuntimeState.SetRuntimeState(
                    useOfflineDatabase: false,
                    isOnlineAvailable: false,
                    isOfflineDatabaseAvailable: false,
                    statusMessage: errorMessage);

                return new OfflineSyncInitializationResult(false, false, 0, errorMessage);
            }
            finally
            {
                lock (_syncGate)
                {
                    _syncInProgress = false;
                }
            }
        }

        public static void QueueEmployeeUpsert(int employeeId) =>
            QueueChange("Employee", employeeId, BuildScopeKey(employeeId), deleteExistingScopeOperations: false, operationType: "Upsert");

        public static void QueueEmployeeDelete(int employeeId) =>
            QueueChange("Employee", employeeId, BuildScopeKey(employeeId), deleteExistingScopeOperations: true, operationType: "Delete");

        public static void QueueAttendanceUpsert(int attendanceId, int employeeId) =>
            QueueChange("Attendance", attendanceId, BuildScopeKey(employeeId), deleteExistingScopeOperations: false, operationType: "Upsert");

        public static void QueueAttendanceDelete(int attendanceId, int employeeId) =>
            QueueChange("Attendance", attendanceId, BuildScopeKey(employeeId), deleteExistingScopeOperations: false, operationType: "Delete");

        public static void QueuePayrollUpsert(int payrollId, int employeeId) =>
            QueueChange("Payroll", payrollId, BuildScopeKey(employeeId), deleteExistingScopeOperations: false, operationType: "Upsert");

        public static void QueuePayrollDelete(int payrollId, int employeeId) =>
            QueueChange("Payroll", payrollId, BuildScopeKey(employeeId), deleteExistingScopeOperations: false, operationType: "Delete");

        public static void QueueLeaveRequestUpsert(int leaveRequestId, int employeeId) =>
            QueueChange("LeaveRequest", leaveRequestId, BuildScopeKey(employeeId), deleteExistingScopeOperations: false, operationType: "Upsert");

        public static void QueueLeaveRequestDelete(int leaveRequestId, int employeeId) =>
            QueueChange("LeaveRequest", leaveRequestId, BuildScopeKey(employeeId), deleteExistingScopeOperations: false, operationType: "Delete");

        public static void QueueBrandingUpsert() =>
            QueueChange("Branding", AppBranding.DefaultBrandingSettingsId, "branding", deleteExistingScopeOperations: false, operationType: "Upsert");

        public static int GetPendingOperationCount()
        {
            if (!DatabaseHelper.IsOfflineConfigured() || !DatabaseHelper.CanConnect(DatabaseConnectionTarget.Offline, out _))
            {
                return 0;
            }

            using var connection = DatabaseHelper.GetConnection(DatabaseConnectionTarget.Offline);
            using var command = new MySqlCommand($"SELECT COUNT(*) FROM {SyncQueueTableName}", connection);
            connection.Open();
            return Convert.ToInt32(command.ExecuteScalar());
        }

        private static string? EnsureOnlineDatabaseReady()
        {
            var authRepository = new AuthRepository();
            authRepository.EnsureAuthSchemaAndSeedDefaults(DatabaseConnectionTarget.Online);
            string? schoolSyncSummary;

            try
            {
                var schoolSyncResult = new SchoolTeacherSyncService().SyncTeachers();
                schoolSyncSummary = schoolSyncResult.WasSkipped ? null : schoolSyncResult.ToSummary();
            }
            catch (Exception ex)
            {
                schoolSyncSummary = $"School sync warning: {ex.Message}";
            }

            authRepository.EnsureEmployeeAccounts(DatabaseConnectionTarget.Online);
            return schoolSyncSummary;
        }

        private static bool EnsureOfflineDatabaseReady(out string message)
        {
            if (!DatabaseHelper.IsOfflineConfigured())
            {
                message = $"Local {GetOfflineStoreLabel()} is not configured.";
                return false;
            }

            if (!DatabaseHelper.CanConnect(DatabaseConnectionTarget.Offline, out message))
            {
                return false;
            }

            var authRepository = new AuthRepository();
            authRepository.EnsureLocalAuthSchema(DatabaseConnectionTarget.Offline);

            using var connection = DatabaseHelper.GetConnection(DatabaseConnectionTarget.Offline);
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                DatabaseHelper.EnsureCoreSchema(connection, transaction);
                EnsureSyncQueueTable(connection, transaction);
                EnsureOfflineIdentityFloor(connection, transaction, "Employees", "EmployeeId");
                EnsureOfflineIdentityFloor(connection, transaction, "AttendanceRecords", "AttendanceId");
                EnsureOfflineIdentityFloor(connection, transaction, "PayrollRecords", "PayrollId");
                EnsureOfflineIdentityFloor(connection, transaction, "LeaveRequests", "LeaveRequestId");
                EnsureOfflineIdentityFloor(connection, transaction, "UserAccounts", "UserAccountId");
                transaction.Commit();
                return true;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        private static bool HasOfflineLoginData()
        {
            if (!DatabaseHelper.IsOfflineConfigured() || !DatabaseHelper.CanConnect(DatabaseConnectionTarget.Offline, out _))
            {
                return false;
            }

            using var connection = DatabaseHelper.GetConnection(DatabaseConnectionTarget.Offline);
            using var command = new MySqlCommand("SELECT COUNT(*) FROM UserAccounts", connection);
            connection.Open();
            return Convert.ToInt32(command.ExecuteScalar()) > 0;
        }

        private static void QueueChange(string entityType, int recordId, string scopeKey, bool deleteExistingScopeOperations, string operationType)
        {
            if (!DatabaseRuntimeState.UseOfflineDatabase || !DatabaseRuntimeState.IsOfflineDatabaseAvailable)
            {
                return;
            }

            using var connection = DatabaseHelper.GetConnection(DatabaseConnectionTarget.Offline);
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                EnsureSyncQueueTable(connection, transaction);

                if (deleteExistingScopeOperations)
                {
                    using var deleteScope = new MySqlCommand(
                        $"DELETE FROM {SyncQueueTableName} WHERE ScopeKey = @ScopeKey",
                        connection,
                        transaction);
                    deleteScope.Parameters.AddWithValue("@ScopeKey", scopeKey);
                    deleteScope.ExecuteNonQuery();
                }

                using var command = new MySqlCommand($@"
                    INSERT INTO {SyncQueueTableName} (EntityType, OperationType, RecordId, ScopeKey)
                    VALUES (@EntityType, @OperationType, @RecordId, @ScopeKey)
                    {(connection.Provider == DatabaseProvider.Sqlite
                        ? "ON CONFLICT(EntityType, RecordId) DO UPDATE SET OperationType = excluded.OperationType, ScopeKey = excluded.ScopeKey, CreatedAt = CURRENT_TIMESTAMP"
                        : "ON DUPLICATE KEY UPDATE OperationType = VALUES(OperationType), ScopeKey = VALUES(ScopeKey), CreatedAt = CURRENT_TIMESTAMP")}", connection, transaction);

                command.Parameters.AddWithValue("@EntityType", entityType);
                command.Parameters.AddWithValue("@OperationType", operationType);
                command.Parameters.AddWithValue("@RecordId", recordId);
                command.Parameters.AddWithValue("@ScopeKey", scopeKey);
                command.ExecuteNonQuery();

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        private static void SynchronizePendingChanges()
        {
            var operations = LoadPendingOperations();
            if (operations.Count == 0)
            {
                return;
            }

            AppLogger.Sync($"Synchronizing {operations.Count} pending operations to online database.");

            using var localConnection = DatabaseHelper.GetConnection(DatabaseConnectionTarget.Offline);
            using var onlineConnection = DatabaseHelper.GetConnection(DatabaseConnectionTarget.Online);
            localConnection.Open();
            onlineConnection.Open();
            using var onlineTransaction = onlineConnection.BeginTransaction();

            try
            {
                foreach (var operation in operations)
                {
                    switch (operation.EntityType)
                    {
                        case "Branding":
                            ApplyBrandingOperation(localConnection, onlineConnection, onlineTransaction, operation);
                            break;
                        case "Employee":
                            ApplyEmployeeOperation(localConnection, onlineConnection, onlineTransaction, operation);
                            break;
                        case "LeaveRequest":
                            ApplyLeaveRequestOperation(localConnection, onlineConnection, onlineTransaction, operation);
                            break;
                        case "Attendance":
                            ApplyAttendanceOperation(localConnection, onlineConnection, onlineTransaction, operation);
                            break;
                        case "Payroll":
                            ApplyPayrollOperation(localConnection, onlineConnection, onlineTransaction, operation);
                            break;
                    }
                }

                onlineTransaction.Commit();
                ClearPendingOperations();
                AppLogger.Sync($"Successfully synchronized {operations.Count} operations.");
                new AuthRepository().EnsureEmployeeAccounts(DatabaseConnectionTarget.Online);
            }
            catch
            {
                onlineTransaction.Rollback();
                throw;
            }
        }

        private static void RefreshOfflineMirrorFromOnline()
        {
            AppLogger.Sync("Refreshing offline mirror from online database.");
            using var onlineConnection = DatabaseHelper.GetConnection(DatabaseConnectionTarget.Online);
            using var offlineConnection = DatabaseHelper.GetConnection(DatabaseConnectionTarget.Offline);
            onlineConnection.Open();
            offlineConnection.Open();
            using var transaction = offlineConnection.BeginTransaction();

            try
            {
                EnsureSyncQueueTable(offlineConnection, transaction);

                using (var disableForeignKeys = new MySqlCommand(
                    offlineConnection.Provider == DatabaseProvider.Sqlite
                        ? "PRAGMA foreign_keys = OFF;"
                        : "SET FOREIGN_KEY_CHECKS = 0;",
                    offlineConnection,
                    transaction))
                {
                    disableForeignKeys.ExecuteNonQuery();
                }

                foreach (var table in new[] { "UserAccounts", "LeaveRequests", "AttendanceRecords", "PayrollRecords", "Employees", "AppBrandingSettings" })
                {
                    using var clearTable = new MySqlCommand($"DELETE FROM {table};", offlineConnection, transaction);
                    clearTable.ExecuteNonQuery();
                }

                CopyBrandingSettings(onlineConnection, offlineConnection, transaction);
                CopyEmployees(onlineConnection, offlineConnection, transaction);
                CopyLeaveRequests(onlineConnection, offlineConnection, transaction);
                CopyAttendanceRecords(onlineConnection, offlineConnection, transaction);
                CopyPayrollRecords(onlineConnection, offlineConnection, transaction);
                CopyUserAccounts(onlineConnection, offlineConnection, transaction);

                EnsureOfflineIdentityFloor(offlineConnection, transaction, "Employees", "EmployeeId");
                EnsureOfflineIdentityFloor(offlineConnection, transaction, "LeaveRequests", "LeaveRequestId");
                EnsureOfflineIdentityFloor(offlineConnection, transaction, "AttendanceRecords", "AttendanceId");
                EnsureOfflineIdentityFloor(offlineConnection, transaction, "PayrollRecords", "PayrollId");
                EnsureOfflineIdentityFloor(offlineConnection, transaction, "UserAccounts", "UserAccountId");

                using (var enableForeignKeys = new MySqlCommand(
                    offlineConnection.Provider == DatabaseProvider.Sqlite
                        ? "PRAGMA foreign_keys = ON;"
                        : "SET FOREIGN_KEY_CHECKS = 1;",
                    offlineConnection,
                    transaction))
                {
                    enableForeignKeys.ExecuteNonQuery();
                }

                transaction.Commit();
                _lastOfflineMirrorRefreshUtc = DateTime.UtcNow;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        private static void CopyBrandingSettings(MySqlConnection onlineConnection, MySqlConnection offlineConnection, MySqlTransaction transaction)
        {
            using var selectCommand = new MySqlCommand(@"
                SELECT BrandingSettingsId, LogoImage
                FROM AppBrandingSettings
                ORDER BY BrandingSettingsId", onlineConnection);

            using var reader = selectCommand.ExecuteReader();
            while (reader.Read())
            {
                using var insertCommand = new MySqlCommand(@"
                    INSERT INTO AppBrandingSettings
                    (BrandingSettingsId, LogoImage)
                    VALUES
                    (@BrandingSettingsId, @LogoImage)",
                    offlineConnection,
                    transaction);

                insertCommand.Parameters.AddWithValue("@BrandingSettingsId", Convert.ToInt32(reader["BrandingSettingsId"]));
                insertCommand.Parameters.AddWithValue("@LogoImage", reader["LogoImage"] is DBNull ? DBNull.Value : reader["LogoImage"]);
                insertCommand.ExecuteNonQuery();
            }
        }

        private static void CopyEmployees(MySqlConnection onlineConnection, MySqlConnection offlineConnection, MySqlTransaction transaction)
        {
            using var selectCommand = new MySqlCommand(@"
                SELECT EmployeeId, EmployeeCode, FullName, Email, Phone, Position, Department,
                       HourlyRate, HireDate, IsActive, SourceTeacherId, SourceUserId, ProfileImage, BiometricTemplate
                FROM Employees
                ORDER BY EmployeeId", onlineConnection);

            using var reader = selectCommand.ExecuteReader();
            while (reader.Read())
            {
                using var insertCommand = new MySqlCommand(@"
                    INSERT INTO Employees
                    (EmployeeId, EmployeeCode, FullName, Email, Phone, Position, Department, HourlyRate, HireDate, IsActive, SourceTeacherId, SourceUserId, ProfileImage, BiometricTemplate)
                    VALUES
                    (@EmployeeId, @EmployeeCode, @FullName, @Email, @Phone, @Position, @Department, @HourlyRate, @HireDate, @IsActive, @SourceTeacherId, @SourceUserId, @ProfileImage, @BiometricTemplate)",
                    offlineConnection,
                    transaction);

                insertCommand.Parameters.AddWithValue("@EmployeeId", Convert.ToInt32(reader["EmployeeId"]));
                insertCommand.Parameters.AddWithValue("@EmployeeCode", Convert.ToString(reader["EmployeeCode"]) ?? string.Empty);
                insertCommand.Parameters.AddWithValue("@FullName", Convert.ToString(reader["FullName"]) ?? string.Empty);
                insertCommand.Parameters.AddWithValue("@Email", reader["Email"] is DBNull ? DBNull.Value : reader["Email"]);
                insertCommand.Parameters.AddWithValue("@Phone", reader["Phone"] is DBNull ? DBNull.Value : reader["Phone"]);
                insertCommand.Parameters.AddWithValue("@Position", reader["Position"] is DBNull ? DBNull.Value : reader["Position"]);
                insertCommand.Parameters.AddWithValue("@Department", reader["Department"] is DBNull ? DBNull.Value : reader["Department"]);
                insertCommand.Parameters.AddWithValue("@HourlyRate", Convert.ToDecimal(reader["HourlyRate"]));
                insertCommand.Parameters.AddWithValue("@HireDate", Convert.ToDateTime(reader["HireDate"]).Date);
                insertCommand.Parameters.AddWithValue("@IsActive", Convert.ToBoolean(reader["IsActive"]));
                insertCommand.Parameters.AddWithValue("@SourceTeacherId", reader["SourceTeacherId"] is DBNull ? DBNull.Value : reader["SourceTeacherId"]);
                insertCommand.Parameters.AddWithValue("@SourceUserId", reader["SourceUserId"] is DBNull ? DBNull.Value : reader["SourceUserId"]);
                insertCommand.Parameters.AddWithValue("@ProfileImage", reader["ProfileImage"] is DBNull ? DBNull.Value : reader["ProfileImage"]);
                insertCommand.Parameters.AddWithValue("@BiometricTemplate", reader["BiometricTemplate"] is DBNull ? DBNull.Value : reader["BiometricTemplate"]);
                insertCommand.ExecuteNonQuery();
            }
        }

        private static void CopyLeaveRequests(MySqlConnection onlineConnection, MySqlConnection offlineConnection, MySqlTransaction transaction)
        {
            using var selectCommand = new MySqlCommand(@"
                SELECT LeaveRequestId, EmployeeId, LeaveType, IsPaid, StartDate, EndDate, Reason, Status,
                       ReviewerNotes, ReviewedBy, ReviewedAt, CreatedAt, UpdatedAt
                FROM LeaveRequests
                ORDER BY LeaveRequestId", onlineConnection);

            using var reader = selectCommand.ExecuteReader();
            while (reader.Read())
            {
                using var insertCommand = new MySqlCommand(@"
                    INSERT INTO LeaveRequests
                    (LeaveRequestId, EmployeeId, LeaveType, IsPaid, StartDate, EndDate, Reason, Status, ReviewerNotes, ReviewedBy, ReviewedAt, CreatedAt, UpdatedAt)
                    VALUES
                    (@LeaveRequestId, @EmployeeId, @LeaveType, @IsPaid, @StartDate, @EndDate, @Reason, @Status, @ReviewerNotes, @ReviewedBy, @ReviewedAt, @CreatedAt, @UpdatedAt)",
                    offlineConnection,
                    transaction);

                insertCommand.Parameters.AddWithValue("@LeaveRequestId", Convert.ToInt32(reader["LeaveRequestId"]));
                insertCommand.Parameters.AddWithValue("@EmployeeId", Convert.ToInt32(reader["EmployeeId"]));
                insertCommand.Parameters.AddWithValue("@LeaveType", Convert.ToString(reader["LeaveType"]) ?? string.Empty);
                insertCommand.Parameters.AddWithValue("@IsPaid", Convert.ToBoolean(reader["IsPaid"]));
                insertCommand.Parameters.AddWithValue("@StartDate", Convert.ToDateTime(reader["StartDate"]).Date);
                insertCommand.Parameters.AddWithValue("@EndDate", Convert.ToDateTime(reader["EndDate"]).Date);
                insertCommand.Parameters.AddWithValue("@Reason", Convert.ToString(reader["Reason"]) ?? string.Empty);
                insertCommand.Parameters.AddWithValue("@Status", Convert.ToString(reader["Status"]) ?? string.Empty);
                insertCommand.Parameters.AddWithValue("@ReviewerNotes", reader["ReviewerNotes"] is DBNull ? DBNull.Value : reader["ReviewerNotes"]);
                insertCommand.Parameters.AddWithValue("@ReviewedBy", reader["ReviewedBy"] is DBNull ? DBNull.Value : reader["ReviewedBy"]);
                insertCommand.Parameters.AddWithValue("@ReviewedAt", reader["ReviewedAt"] is DBNull ? DBNull.Value : reader["ReviewedAt"]);
                insertCommand.Parameters.AddWithValue("@CreatedAt", reader["CreatedAt"] is DBNull ? DateTime.UtcNow : reader["CreatedAt"]);
                insertCommand.Parameters.AddWithValue("@UpdatedAt", reader["UpdatedAt"] is DBNull ? DateTime.UtcNow : reader["UpdatedAt"]);
                insertCommand.ExecuteNonQuery();
            }
        }

        private static void CopyAttendanceRecords(MySqlConnection onlineConnection, MySqlConnection offlineConnection, MySqlTransaction transaction)
        {
            using var selectCommand = new MySqlCommand(@"
                SELECT AttendanceId, EmployeeId, AttendanceDate, TimeInAM, TimeOutAM, TimeInPM, TimeOutPM, Status, IsBiometricVerified
                FROM AttendanceRecords
                ORDER BY AttendanceId", onlineConnection);

            using var reader = selectCommand.ExecuteReader();
            while (reader.Read())
            {
                using var insertCommand = new MySqlCommand(@"
                    INSERT INTO AttendanceRecords
                    (AttendanceId, EmployeeId, AttendanceDate, TimeInAM, TimeOutAM, TimeInPM, TimeOutPM, Status, IsBiometricVerified)
                    VALUES
                    (@AttendanceId, @EmployeeId, @AttendanceDate, @TimeInAM, @TimeOutAM, @TimeInPM, @TimeOutPM, @Status, @IsBiometricVerified)",
                    offlineConnection,
                    transaction);

                insertCommand.Parameters.AddWithValue("@AttendanceId", Convert.ToInt32(reader["AttendanceId"]));
                insertCommand.Parameters.AddWithValue("@EmployeeId", Convert.ToInt32(reader["EmployeeId"]));
                insertCommand.Parameters.AddWithValue("@AttendanceDate", Convert.ToDateTime(reader["AttendanceDate"]).Date);
                insertCommand.Parameters.AddWithValue("@TimeInAM", reader["TimeInAM"] is DBNull ? DBNull.Value : reader["TimeInAM"]);
                insertCommand.Parameters.AddWithValue("@TimeOutAM", reader["TimeOutAM"] is DBNull ? DBNull.Value : reader["TimeOutAM"]);
                insertCommand.Parameters.AddWithValue("@TimeInPM", reader["TimeInPM"] is DBNull ? DBNull.Value : reader["TimeInPM"]);
                insertCommand.Parameters.AddWithValue("@TimeOutPM", reader["TimeOutPM"] is DBNull ? DBNull.Value : reader["TimeOutPM"]);
                insertCommand.Parameters.AddWithValue("@Status", Convert.ToString(reader["Status"]) ?? string.Empty);
                insertCommand.Parameters.AddWithValue("@IsBiometricVerified", Convert.ToBoolean(reader["IsBiometricVerified"]));
                insertCommand.ExecuteNonQuery();
            }
        }

        private static void CopyPayrollRecords(MySqlConnection onlineConnection, MySqlConnection offlineConnection, MySqlTransaction transaction)
        {
            using var selectCommand = new MySqlCommand(@"
                SELECT PayrollId, EmployeeId, PayPeriodStart, PayPeriodEnd, RegularHours, OvertimeHours,
                       GrossPay, Deductions, NetPay, ManualDeduction, ManualDeductionNote, Status, CreatedAt
                FROM PayrollRecords
                ORDER BY PayrollId", onlineConnection);

            using var reader = selectCommand.ExecuteReader();
            while (reader.Read())
            {
                using var insertCommand = new MySqlCommand(@"
                    INSERT INTO PayrollRecords
                    (PayrollId, EmployeeId, PayPeriodStart, PayPeriodEnd, RegularHours, OvertimeHours, GrossPay, Deductions, NetPay, ManualDeduction, ManualDeductionNote, Status, CreatedAt)
                    VALUES
                    (@PayrollId, @EmployeeId, @PayPeriodStart, @PayPeriodEnd, @RegularHours, @OvertimeHours, @GrossPay, @Deductions, @NetPay, @ManualDeduction, @ManualDeductionNote, @Status, @CreatedAt)",
                    offlineConnection,
                    transaction);

                insertCommand.Parameters.AddWithValue("@PayrollId", Convert.ToInt32(reader["PayrollId"]));
                insertCommand.Parameters.AddWithValue("@EmployeeId", Convert.ToInt32(reader["EmployeeId"]));
                insertCommand.Parameters.AddWithValue("@PayPeriodStart", Convert.ToDateTime(reader["PayPeriodStart"]).Date);
                insertCommand.Parameters.AddWithValue("@PayPeriodEnd", Convert.ToDateTime(reader["PayPeriodEnd"]).Date);
                insertCommand.Parameters.AddWithValue("@RegularHours", Convert.ToDecimal(reader["RegularHours"]));
                insertCommand.Parameters.AddWithValue("@OvertimeHours", Convert.ToDecimal(reader["OvertimeHours"]));
                insertCommand.Parameters.AddWithValue("@GrossPay", Convert.ToDecimal(reader["GrossPay"]));
                insertCommand.Parameters.AddWithValue("@Deductions", Convert.ToDecimal(reader["Deductions"]));
                insertCommand.Parameters.AddWithValue("@NetPay", Convert.ToDecimal(reader["NetPay"]));
                insertCommand.Parameters.AddWithValue("@ManualDeduction", reader["ManualDeduction"] is DBNull ? 0m : Convert.ToDecimal(reader["ManualDeduction"]));
                insertCommand.Parameters.AddWithValue("@ManualDeductionNote", reader["ManualDeductionNote"] is DBNull ? string.Empty : Convert.ToString(reader["ManualDeductionNote"]) ?? string.Empty);
                insertCommand.Parameters.AddWithValue("@Status", Convert.ToString(reader["Status"]) ?? string.Empty);
                insertCommand.Parameters.AddWithValue("@CreatedAt", reader["CreatedAt"] is DBNull ? DateTime.UtcNow : reader["CreatedAt"]);
                insertCommand.ExecuteNonQuery();
            }
        }

        private static void CopyUserAccounts(MySqlConnection onlineConnection, MySqlConnection offlineConnection, MySqlTransaction transaction)
        {
            using var selectCommand = new MySqlCommand(@"
                SELECT UserAccountId, Username, PasswordHash, PasswordSalt, HashIterations, Role, EmployeeId, IsActive, CreatedAt
                FROM UserAccounts
                ORDER BY UserAccountId", onlineConnection);

            using var reader = selectCommand.ExecuteReader();
            while (reader.Read())
            {
                using var insertCommand = new MySqlCommand(@"
                    INSERT INTO UserAccounts
                    (UserAccountId, Username, PasswordHash, PasswordSalt, HashIterations, Role, EmployeeId, IsActive, CreatedAt)
                    VALUES
                    (@UserAccountId, @Username, @PasswordHash, @PasswordSalt, @HashIterations, @Role, @EmployeeId, @IsActive, @CreatedAt)",
                    offlineConnection,
                    transaction);

                insertCommand.Parameters.AddWithValue("@UserAccountId", Convert.ToInt32(reader["UserAccountId"]));
                insertCommand.Parameters.AddWithValue("@Username", Convert.ToString(reader["Username"]) ?? string.Empty);
                insertCommand.Parameters.AddWithValue("@PasswordHash", Convert.ToString(reader["PasswordHash"]) ?? string.Empty);
                insertCommand.Parameters.AddWithValue("@PasswordSalt", reader["PasswordSalt"] is DBNull ? DBNull.Value : reader["PasswordSalt"]);
                insertCommand.Parameters.AddWithValue("@HashIterations", reader["HashIterations"] is DBNull ? 210000 : reader["HashIterations"]);
                insertCommand.Parameters.AddWithValue("@Role", Convert.ToString(reader["Role"]) ?? string.Empty);
                insertCommand.Parameters.AddWithValue("@EmployeeId", reader["EmployeeId"] is DBNull ? DBNull.Value : reader["EmployeeId"]);
                insertCommand.Parameters.AddWithValue("@IsActive", Convert.ToBoolean(reader["IsActive"]));
                insertCommand.Parameters.AddWithValue("@CreatedAt", reader["CreatedAt"] is DBNull ? DateTime.UtcNow : reader["CreatedAt"]);
                insertCommand.ExecuteNonQuery();
            }
        }

        private static void ApplyEmployeeOperation(
            MySqlConnection localConnection,
            MySqlConnection onlineConnection,
            MySqlTransaction onlineTransaction,
            PendingSyncOperation operation)
        {
            if (string.Equals(operation.OperationType, "Delete", StringComparison.OrdinalIgnoreCase))
            {
                using var deleteAccounts = new MySqlCommand("DELETE FROM UserAccounts WHERE EmployeeId = @EmployeeId", onlineConnection, onlineTransaction);
                deleteAccounts.Parameters.AddWithValue("@EmployeeId", operation.RecordId);
                deleteAccounts.ExecuteNonQuery();

                using var deletePayroll = new MySqlCommand("DELETE FROM PayrollRecords WHERE EmployeeId = @EmployeeId", onlineConnection, onlineTransaction);
                deletePayroll.Parameters.AddWithValue("@EmployeeId", operation.RecordId);
                deletePayroll.ExecuteNonQuery();

                using var deleteAttendance = new MySqlCommand("DELETE FROM AttendanceRecords WHERE EmployeeId = @EmployeeId", onlineConnection, onlineTransaction);
                deleteAttendance.Parameters.AddWithValue("@EmployeeId", operation.RecordId);
                deleteAttendance.ExecuteNonQuery();

                using var deleteEmployee = new MySqlCommand("DELETE FROM Employees WHERE EmployeeId = @EmployeeId", onlineConnection, onlineTransaction);
                deleteEmployee.Parameters.AddWithValue("@EmployeeId", operation.RecordId);
                deleteEmployee.ExecuteNonQuery();
                return;
            }

            var employee = LoadLocalEmployee(localConnection, operation.RecordId);
            if (employee == null)
            {
                return;
            }

            using var command = new MySqlCommand(@"
                INSERT INTO Employees
                (EmployeeId, EmployeeCode, FullName, Email, Phone, Position, Department, HourlyRate, HireDate, IsActive, SourceTeacherId, SourceUserId, ProfileImage, BiometricTemplate)
                VALUES
                (@EmployeeId, @EmployeeCode, @FullName, @Email, @Phone, @Position, @Department, @HourlyRate, @HireDate, @IsActive, @SourceTeacherId, @SourceUserId, @ProfileImage, @BiometricTemplate)
                ON DUPLICATE KEY UPDATE
                    EmployeeCode = VALUES(EmployeeCode),
                    FullName = VALUES(FullName),
                    Email = VALUES(Email),
                    Phone = VALUES(Phone),
                    Position = VALUES(Position),
                    Department = VALUES(Department),
                    HourlyRate = VALUES(HourlyRate),
                    HireDate = VALUES(HireDate),
                    IsActive = VALUES(IsActive),
                    SourceTeacherId = VALUES(SourceTeacherId),
                    SourceUserId = VALUES(SourceUserId),
                    ProfileImage = VALUES(ProfileImage),
                    BiometricTemplate = VALUES(BiometricTemplate)",
                onlineConnection,
                onlineTransaction);

            command.Parameters.AddWithValue("@EmployeeId", employee.EmployeeId);
            AddEmployeeParameters(command, employee);
            command.ExecuteNonQuery();
        }

        private static void ApplyBrandingOperation(
            MySqlConnection localConnection,
            MySqlConnection onlineConnection,
            MySqlTransaction onlineTransaction,
            PendingSyncOperation operation)
        {
            var branding = LoadLocalBranding(localConnection, operation.RecordId);
            if (branding == null)
            {
                return;
            }

            using var command = new MySqlCommand(@"
                INSERT INTO AppBrandingSettings
                (BrandingSettingsId, LogoImage)
                VALUES
                (@BrandingSettingsId, @LogoImage)
                ON DUPLICATE KEY UPDATE
                    LogoImage = VALUES(LogoImage)",
                onlineConnection,
                onlineTransaction);

            command.Parameters.AddWithValue("@BrandingSettingsId", branding.BrandingSettingsId);
            command.Parameters.AddWithValue("@LogoImage", branding.LogoImage is null ? DBNull.Value : branding.LogoImage);
            command.ExecuteNonQuery();
        }

        private static void ApplyLeaveRequestOperation(
            MySqlConnection localConnection,
            MySqlConnection onlineConnection,
            MySqlTransaction onlineTransaction,
            PendingSyncOperation operation)
        {
            if (string.Equals(operation.OperationType, "Delete", StringComparison.OrdinalIgnoreCase))
            {
                using var deleteCommand = new MySqlCommand(
                    "DELETE FROM LeaveRequests WHERE LeaveRequestId = @LeaveRequestId",
                    onlineConnection,
                    onlineTransaction);
                deleteCommand.Parameters.AddWithValue("@LeaveRequestId", operation.RecordId);
                deleteCommand.ExecuteNonQuery();
                return;
            }

            var leaveRequest = LoadLocalLeaveRequest(localConnection, operation.RecordId);
            if (leaveRequest == null)
            {
                return;
            }

            if (!OnlineRecordExists(onlineConnection, onlineTransaction, "Employees", "EmployeeId", leaveRequest.EmployeeId))
            {
                ApplyEmployeeOperation(
                    localConnection,
                    onlineConnection,
                    onlineTransaction,
                    new PendingSyncOperation(0, "Employee", "Upsert", leaveRequest.EmployeeId, BuildScopeKey(leaveRequest.EmployeeId)));
            }

            using var command = new MySqlCommand(@"
                INSERT INTO LeaveRequests
                (LeaveRequestId, EmployeeId, LeaveType, IsPaid, StartDate, EndDate, Reason, Status, ReviewerNotes, ReviewedBy, ReviewedAt, CreatedAt, UpdatedAt)
                VALUES
                (@LeaveRequestId, @EmployeeId, @LeaveType, @IsPaid, @StartDate, @EndDate, @Reason, @Status, @ReviewerNotes, @ReviewedBy, @ReviewedAt, @CreatedAt, @UpdatedAt)
                ON DUPLICATE KEY UPDATE
                    EmployeeId = VALUES(EmployeeId),
                    LeaveType = VALUES(LeaveType),
                    IsPaid = VALUES(IsPaid),
                    StartDate = VALUES(StartDate),
                    EndDate = VALUES(EndDate),
                    Reason = VALUES(Reason),
                    Status = VALUES(Status),
                    ReviewerNotes = VALUES(ReviewerNotes),
                    ReviewedBy = VALUES(ReviewedBy),
                    ReviewedAt = VALUES(ReviewedAt),
                    UpdatedAt = VALUES(UpdatedAt)",
                onlineConnection,
                onlineTransaction);

            command.Parameters.AddWithValue("@LeaveRequestId", leaveRequest.LeaveRequestId);
            AddLeaveRequestParameters(command, leaveRequest);
            command.Parameters.AddWithValue("@CreatedAt", leaveRequest.CreatedAt);
            command.Parameters.AddWithValue("@UpdatedAt", leaveRequest.UpdatedAt);
            command.ExecuteNonQuery();
        }

        private static void ApplyAttendanceOperation(
            MySqlConnection localConnection,
            MySqlConnection onlineConnection,
            MySqlTransaction onlineTransaction,
            PendingSyncOperation operation)
        {
            if (string.Equals(operation.OperationType, "Delete", StringComparison.OrdinalIgnoreCase))
            {
                using var deleteCommand = new MySqlCommand(
                    "DELETE FROM AttendanceRecords WHERE AttendanceId = @AttendanceId",
                    onlineConnection,
                    onlineTransaction);
                deleteCommand.Parameters.AddWithValue("@AttendanceId", operation.RecordId);
                deleteCommand.ExecuteNonQuery();
                return;
            }

            var attendance = LoadLocalAttendance(localConnection, operation.RecordId);
            if (attendance == null)
            {
                return;
            }

            if (!OnlineRecordExists(onlineConnection, onlineTransaction, "Employees", "EmployeeId", attendance.EmployeeId))
            {
                ApplyEmployeeOperation(
                    localConnection,
                    onlineConnection,
                    onlineTransaction,
                    new PendingSyncOperation(0, "Employee", "Upsert", attendance.EmployeeId, BuildScopeKey(attendance.EmployeeId)));
            }

            using var command = new MySqlCommand(@"
                INSERT INTO AttendanceRecords
                (AttendanceId, EmployeeId, AttendanceDate, TimeInAM, TimeOutAM, TimeInPM, TimeOutPM, Status, IsBiometricVerified)
                VALUES
                (@AttendanceId, @EmployeeId, @AttendanceDate, @TimeInAM, @TimeOutAM, @TimeInPM, @TimeOutPM, @Status, @IsBiometricVerified)
                ON DUPLICATE KEY UPDATE
                    EmployeeId = VALUES(EmployeeId),
                    AttendanceDate = VALUES(AttendanceDate),
                    TimeInAM = VALUES(TimeInAM),
                    TimeOutAM = VALUES(TimeOutAM),
                    TimeInPM = VALUES(TimeInPM),
                    TimeOutPM = VALUES(TimeOutPM),
                    Status = VALUES(Status),
                    IsBiometricVerified = VALUES(IsBiometricVerified)",
                onlineConnection,
                onlineTransaction);

            command.Parameters.AddWithValue("@AttendanceId", attendance.AttendanceId);
            command.Parameters.AddWithValue("@EmployeeId", attendance.EmployeeId);
            command.Parameters.AddWithValue("@AttendanceDate", attendance.AttendanceDate.Date);
            command.Parameters.AddWithValue("@TimeInAM", attendance.TimeInAM.HasValue ? attendance.TimeInAM.Value : DBNull.Value);
            command.Parameters.AddWithValue("@TimeOutAM", attendance.TimeOutAM.HasValue ? attendance.TimeOutAM.Value : DBNull.Value);
            command.Parameters.AddWithValue("@TimeInPM", attendance.TimeInPM.HasValue ? attendance.TimeInPM.Value : DBNull.Value);
            command.Parameters.AddWithValue("@TimeOutPM", attendance.TimeOutPM.HasValue ? attendance.TimeOutPM.Value : DBNull.Value);
            command.Parameters.AddWithValue("@Status", attendance.Status);
            command.Parameters.AddWithValue("@IsBiometricVerified", attendance.IsBiometricVerified);
            command.ExecuteNonQuery();
        }

        private static void ApplyPayrollOperation(
            MySqlConnection localConnection,
            MySqlConnection onlineConnection,
            MySqlTransaction onlineTransaction,
            PendingSyncOperation operation)
        {
            if (string.Equals(operation.OperationType, "Delete", StringComparison.OrdinalIgnoreCase))
            {
                using var deleteCommand = new MySqlCommand(
                    "DELETE FROM PayrollRecords WHERE PayrollId = @PayrollId",
                    onlineConnection,
                    onlineTransaction);
                deleteCommand.Parameters.AddWithValue("@PayrollId", operation.RecordId);
                deleteCommand.ExecuteNonQuery();
                return;
            }

            var payroll = LoadLocalPayroll(localConnection, operation.RecordId);
            if (payroll == null)
            {
                return;
            }

            if (!OnlineRecordExists(onlineConnection, onlineTransaction, "Employees", "EmployeeId", payroll.EmployeeId))
            {
                ApplyEmployeeOperation(
                    localConnection,
                    onlineConnection,
                    onlineTransaction,
                    new PendingSyncOperation(0, "Employee", "Upsert", payroll.EmployeeId, BuildScopeKey(payroll.EmployeeId)));
            }

            using var command = new MySqlCommand(@"
                INSERT INTO PayrollRecords
                (PayrollId, EmployeeId, PayPeriodStart, PayPeriodEnd, RegularHours, OvertimeHours, GrossPay, Deductions, NetPay, ManualDeduction, ManualDeductionNote, Status, CreatedAt)
                VALUES
                (@PayrollId, @EmployeeId, @PayPeriodStart, @PayPeriodEnd, @RegularHours, @OvertimeHours, @GrossPay, @Deductions, @NetPay, @ManualDeduction, @ManualDeductionNote, @Status, @CreatedAt)
                ON DUPLICATE KEY UPDATE
                    EmployeeId = VALUES(EmployeeId),
                    PayPeriodStart = VALUES(PayPeriodStart),
                    PayPeriodEnd = VALUES(PayPeriodEnd),
                    RegularHours = VALUES(RegularHours),
                    OvertimeHours = VALUES(OvertimeHours),
                    GrossPay = VALUES(GrossPay),
                    Deductions = VALUES(Deductions),
                    NetPay = VALUES(NetPay),
                    ManualDeduction = VALUES(ManualDeduction),
                    ManualDeductionNote = VALUES(ManualDeductionNote),
                    Status = VALUES(Status)",
                onlineConnection,
                onlineTransaction);

            command.Parameters.AddWithValue("@PayrollId", payroll.PayrollId);
            AddPayrollParameters(command, payroll);
            command.Parameters.AddWithValue("@CreatedAt", payroll.CreatedAt);
            command.ExecuteNonQuery();
        }

        private static Employee? LoadLocalEmployee(MySqlConnection connection, int employeeId)
        {
            using var command = new MySqlCommand(@"
                SELECT EmployeeId, EmployeeCode, FullName, Email, Phone, Position, Department,
                       HourlyRate, HireDate, IsActive, SourceTeacherId, SourceUserId, ProfileImage, BiometricTemplate
                FROM Employees
                WHERE EmployeeId = @EmployeeId
                LIMIT 1", connection);

            command.Parameters.AddWithValue("@EmployeeId", employeeId);
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            return new Employee
            {
                EmployeeId = Convert.ToInt32(reader["EmployeeId"]),
                EmployeeCode = Convert.ToString(reader["EmployeeCode"]) ?? string.Empty,
                FullName = Convert.ToString(reader["FullName"]) ?? string.Empty,
                Email = Convert.ToString(reader["Email"]) ?? string.Empty,
                Phone = Convert.ToString(reader["Phone"]) ?? string.Empty,
                Position = Convert.ToString(reader["Position"]) ?? string.Empty,
                Department = Convert.ToString(reader["Department"]) ?? string.Empty,
                HourlyRate = Convert.ToDecimal(reader["HourlyRate"]),
                HireDate = Convert.ToDateTime(reader["HireDate"]),
                IsActive = Convert.ToBoolean(reader["IsActive"]),
                SourceTeacherId = reader["SourceTeacherId"] is DBNull ? null : Convert.ToInt64(reader["SourceTeacherId"]),
                SourceUserId = reader["SourceUserId"] is DBNull ? null : Convert.ToInt64(reader["SourceUserId"]),
                ProfileImage = reader["ProfileImage"] is DBNull ? null : (byte[])reader["ProfileImage"],
                BiometricTemplate = reader["BiometricTemplate"] is DBNull ? null : (byte[])reader["BiometricTemplate"]
            };
        }

        private static AppBranding? LoadLocalBranding(MySqlConnection connection, int brandingSettingsId)
        {
            using var command = new MySqlCommand(@"
                SELECT BrandingSettingsId, LogoImage
                FROM AppBrandingSettings
                WHERE BrandingSettingsId = @BrandingSettingsId
                LIMIT 1", connection);

            command.Parameters.AddWithValue("@BrandingSettingsId", brandingSettingsId);
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            return new AppBranding
            {
                BrandingSettingsId = Convert.ToInt32(reader["BrandingSettingsId"]),
                LogoImage = reader["LogoImage"] is DBNull ? null : (byte[])reader["LogoImage"]
            };
        }

        private static Attendance? LoadLocalAttendance(MySqlConnection connection, int attendanceId)
        {
            using var command = new MySqlCommand(@"
                SELECT AttendanceId, EmployeeId, AttendanceDate, TimeInAM, TimeOutAM, TimeInPM, TimeOutPM, Status, IsBiometricVerified
                FROM AttendanceRecords
                WHERE AttendanceId = @AttendanceId
                LIMIT 1", connection);

            command.Parameters.AddWithValue("@AttendanceId", attendanceId);
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            return new Attendance
            {
                AttendanceId = Convert.ToInt32(reader["AttendanceId"]),
                EmployeeId = Convert.ToInt32(reader["EmployeeId"]),
                AttendanceDate = Convert.ToDateTime(reader["AttendanceDate"]),
                TimeInAM = reader["TimeInAM"] is DBNull ? null : Convert.ToDateTime(reader["TimeInAM"]),
                TimeOutAM = reader["TimeOutAM"] is DBNull ? null : Convert.ToDateTime(reader["TimeOutAM"]),
                TimeInPM = reader["TimeInPM"] is DBNull ? null : Convert.ToDateTime(reader["TimeInPM"]),
                TimeOutPM = reader["TimeOutPM"] is DBNull ? null : Convert.ToDateTime(reader["TimeOutPM"]),
                Status = Convert.ToString(reader["Status"]) ?? string.Empty,
                IsBiometricVerified = Convert.ToBoolean(reader["IsBiometricVerified"])
            };
        }

        private static LocalLeaveRequestRow? LoadLocalLeaveRequest(MySqlConnection connection, int leaveRequestId)
        {
            using var command = new MySqlCommand(@"
                SELECT LeaveRequestId, EmployeeId, LeaveType, IsPaid, StartDate, EndDate, Reason, Status,
                       ReviewerNotes, ReviewedBy, ReviewedAt, CreatedAt, UpdatedAt
                FROM LeaveRequests
                WHERE LeaveRequestId = @LeaveRequestId
                LIMIT 1", connection);

            command.Parameters.AddWithValue("@LeaveRequestId", leaveRequestId);
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            return new LocalLeaveRequestRow
            {
                LeaveRequestId = Convert.ToInt32(reader["LeaveRequestId"]),
                EmployeeId = Convert.ToInt32(reader["EmployeeId"]),
                LeaveType = Convert.ToString(reader["LeaveType"]) ?? string.Empty,
                IsPaid = Convert.ToBoolean(reader["IsPaid"]),
                StartDate = Convert.ToDateTime(reader["StartDate"]),
                EndDate = Convert.ToDateTime(reader["EndDate"]),
                Reason = Convert.ToString(reader["Reason"]) ?? string.Empty,
                Status = Convert.ToString(reader["Status"]) ?? string.Empty,
                ReviewerNotes = Convert.ToString(reader["ReviewerNotes"]) ?? string.Empty,
                ReviewedBy = Convert.ToString(reader["ReviewedBy"]) ?? string.Empty,
                ReviewedAt = reader["ReviewedAt"] is DBNull ? null : Convert.ToDateTime(reader["ReviewedAt"]),
                CreatedAt = reader["CreatedAt"] is DBNull ? DateTime.UtcNow : Convert.ToDateTime(reader["CreatedAt"]),
                UpdatedAt = reader["UpdatedAt"] is DBNull ? DateTime.UtcNow : Convert.ToDateTime(reader["UpdatedAt"])
            };
        }

        private static LocalPayrollRow? LoadLocalPayroll(MySqlConnection connection, int payrollId)
        {
            using var command = new MySqlCommand(@"
                SELECT PayrollId, EmployeeId, PayPeriodStart, PayPeriodEnd, RegularHours, OvertimeHours,
                       GrossPay, Deductions, NetPay, ManualDeduction, ManualDeductionNote, Status, CreatedAt
                FROM PayrollRecords
                WHERE PayrollId = @PayrollId
                LIMIT 1", connection);

            command.Parameters.AddWithValue("@PayrollId", payrollId);
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            return new LocalPayrollRow
            {
                PayrollId = Convert.ToInt32(reader["PayrollId"]),
                EmployeeId = Convert.ToInt32(reader["EmployeeId"]),
                PayPeriodStart = Convert.ToDateTime(reader["PayPeriodStart"]),
                PayPeriodEnd = Convert.ToDateTime(reader["PayPeriodEnd"]),
                RegularHours = Convert.ToDecimal(reader["RegularHours"]),
                OvertimeHours = Convert.ToDecimal(reader["OvertimeHours"]),
                GrossPay = Convert.ToDecimal(reader["GrossPay"]),
                Deductions = Convert.ToDecimal(reader["Deductions"]),
                NetPay = Convert.ToDecimal(reader["NetPay"]),
                ManualDeduction = reader["ManualDeduction"] is DBNull ? 0m : Convert.ToDecimal(reader["ManualDeduction"]),
                ManualDeductionNote = reader["ManualDeductionNote"] is DBNull ? string.Empty : Convert.ToString(reader["ManualDeductionNote"]) ?? string.Empty,
                Status = Convert.ToString(reader["Status"]) ?? string.Empty,
                CreatedAt = reader["CreatedAt"] is DBNull ? DateTime.UtcNow : Convert.ToDateTime(reader["CreatedAt"])
            };
        }

        private static bool OnlineRecordExists(MySqlConnection onlineConnection, MySqlTransaction onlineTransaction, string tableName, string keyColumn, int recordId)
        {
            using var command = new MySqlCommand(
                $"SELECT 1 FROM {tableName} WHERE {keyColumn} = @RecordId LIMIT 1",
                onlineConnection,
                onlineTransaction);
            command.Parameters.AddWithValue("@RecordId", recordId);
            return command.ExecuteScalar() != null;
        }

        private static void EnsureSyncQueueTable(MySqlConnection connection, MySqlTransaction transaction)
        {
            var sql = connection.Provider == DatabaseProvider.Sqlite
                ? $@"
                    CREATE TABLE IF NOT EXISTS {SyncQueueTableName}
                    (
                        SyncQueueId INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                        EntityType TEXT NOT NULL,
                        OperationType TEXT NOT NULL,
                        RecordId INTEGER NOT NULL,
                        ScopeKey TEXT NOT NULL,
                        CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        UNIQUE (EntityType, RecordId)
                    );
                    CREATE INDEX IF NOT EXISTS IX_{SyncQueueTableName}_ScopeCreated ON {SyncQueueTableName} (ScopeKey, CreatedAt);"
                : $@"
                    CREATE TABLE IF NOT EXISTS {SyncQueueTableName}
                    (
                        SyncQueueId BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                        EntityType VARCHAR(30) NOT NULL,
                        OperationType VARCHAR(20) NOT NULL,
                        RecordId INT NOT NULL,
                        ScopeKey VARCHAR(50) NOT NULL,
                        CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        UNIQUE KEY UQ_{SyncQueueTableName}_EntityRecord (EntityType, RecordId),
                        INDEX IX_{SyncQueueTableName}_ScopeCreated (ScopeKey, CreatedAt)
                    ) ENGINE=InnoDB;";
            using var command = new MySqlCommand(sql, connection, transaction);
            command.ExecuteNonQuery();
        }

        private static void EnsureOfflineIdentityFloor(MySqlConnection connection, MySqlTransaction transaction, string tableName, string identityColumn)
        {
            using var maxCommand = new MySqlCommand(
                $"SELECT COALESCE(MAX({identityColumn}), 0) FROM {tableName}",
                connection,
                transaction);
            var currentMax = Convert.ToInt64(maxCommand.ExecuteScalar());
            var nextAutoIncrement = Math.Max(currentMax + 1, OfflineIdentityFloor);

            if (connection.Provider == DatabaseProvider.Sqlite)
            {
                using var updateCommand = new MySqlCommand(
                    "UPDATE sqlite_sequence SET seq = MAX(seq, @NextValue) WHERE name = @TableName",
                    connection,
                    transaction);
                updateCommand.Parameters.AddWithValue("@TableName", tableName);
                updateCommand.Parameters.AddWithValue("@NextValue", nextAutoIncrement - 1);
                var rowsAffected = updateCommand.ExecuteNonQuery();

                if (rowsAffected == 0)
                {
                    using var insertCommand = new MySqlCommand(
                        "INSERT INTO sqlite_sequence(name, seq) VALUES (@TableName, @NextValue)",
                        connection,
                        transaction);
                    insertCommand.Parameters.AddWithValue("@TableName", tableName);
                    insertCommand.Parameters.AddWithValue("@NextValue", nextAutoIncrement - 1);
                    insertCommand.ExecuteNonQuery();
                }

                return;
            }

            using var alterCommand = new MySqlCommand(
                $"ALTER TABLE {tableName} AUTO_INCREMENT = {nextAutoIncrement}",
                connection,
                transaction);
            alterCommand.ExecuteNonQuery();
        }

        private static List<PendingSyncOperation> LoadPendingOperations()
        {
            var operations = new List<PendingSyncOperation>();

            using var connection = DatabaseHelper.GetConnection(DatabaseConnectionTarget.Offline);
            connection.Open();
            using var command = new MySqlCommand($@"
                SELECT SyncQueueId, EntityType, OperationType, RecordId, ScopeKey
                FROM {SyncQueueTableName}
                ORDER BY
                    CASE EntityType
                        WHEN 'Branding' THEN 0
                        WHEN 'Employee' THEN 1
                        WHEN 'LeaveRequest' THEN 2
                        WHEN 'Attendance' THEN 3
                        WHEN 'Payroll' THEN 4
                        ELSE 5
                    END,
                    CreatedAt,
                    SyncQueueId", connection);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                operations.Add(new PendingSyncOperation(
                    SyncQueueId: Convert.ToInt64(reader["SyncQueueId"]),
                    EntityType: Convert.ToString(reader["EntityType"]) ?? string.Empty,
                    OperationType: Convert.ToString(reader["OperationType"]) ?? string.Empty,
                    RecordId: Convert.ToInt32(reader["RecordId"]),
                    ScopeKey: Convert.ToString(reader["ScopeKey"]) ?? string.Empty));
            }

            return operations;
        }

        private static void ClearPendingOperations()
        {
            using var connection = DatabaseHelper.GetConnection(DatabaseConnectionTarget.Offline);
            connection.Open();
            using var command = new MySqlCommand($"DELETE FROM {SyncQueueTableName}", connection);
            command.ExecuteNonQuery();
        }

        private static void AddEmployeeParameters(MySqlCommand command, Employee employee)
        {
            command.Parameters.AddWithValue("@EmployeeCode", employee.EmployeeCode.Trim());
            command.Parameters.AddWithValue("@FullName", employee.FullName.Trim());
            command.Parameters.AddWithValue("@Email", string.IsNullOrWhiteSpace(employee.Email) ? DBNull.Value : employee.Email.Trim());
            command.Parameters.AddWithValue("@Phone", string.IsNullOrWhiteSpace(employee.Phone) ? DBNull.Value : employee.Phone.Trim());
            command.Parameters.AddWithValue("@Position", string.IsNullOrWhiteSpace(employee.Position) ? DBNull.Value : employee.Position.Trim());
            command.Parameters.AddWithValue("@Department", string.IsNullOrWhiteSpace(employee.Department) ? DBNull.Value : employee.Department.Trim());
            command.Parameters.AddWithValue("@HourlyRate", employee.HourlyRate);
            command.Parameters.AddWithValue("@HireDate", employee.HireDate.Date);
            command.Parameters.AddWithValue("@IsActive", employee.IsActive);
            command.Parameters.AddWithValue("@SourceTeacherId", employee.SourceTeacherId.HasValue ? employee.SourceTeacherId.Value : DBNull.Value);
            command.Parameters.AddWithValue("@SourceUserId", employee.SourceUserId.HasValue ? employee.SourceUserId.Value : DBNull.Value);
            command.Parameters.AddWithValue("@ProfileImage", employee.ProfileImage is null ? DBNull.Value : employee.ProfileImage);
            command.Parameters.AddWithValue("@BiometricTemplate", employee.BiometricTemplate is null ? DBNull.Value : employee.BiometricTemplate);
        }

        private static void AddPayrollParameters(MySqlCommand command, LocalPayrollRow payroll)
        {
            command.Parameters.AddWithValue("@EmployeeId", payroll.EmployeeId);
            command.Parameters.AddWithValue("@PayPeriodStart", payroll.PayPeriodStart.Date);
            command.Parameters.AddWithValue("@PayPeriodEnd", payroll.PayPeriodEnd.Date);
            command.Parameters.AddWithValue("@RegularHours", payroll.RegularHours);
            command.Parameters.AddWithValue("@OvertimeHours", payroll.OvertimeHours);
            command.Parameters.AddWithValue("@GrossPay", payroll.GrossPay);
            command.Parameters.AddWithValue("@Deductions", payroll.Deductions);
            command.Parameters.AddWithValue("@NetPay", payroll.NetPay);
            command.Parameters.AddWithValue("@ManualDeduction", payroll.ManualDeduction);
            command.Parameters.AddWithValue("@ManualDeductionNote", payroll.ManualDeductionNote ?? string.Empty);
            command.Parameters.AddWithValue("@Status", payroll.Status);
        }

        private static void AddLeaveRequestParameters(MySqlCommand command, LocalLeaveRequestRow leaveRequest)
        {
            command.Parameters.AddWithValue("@EmployeeId", leaveRequest.EmployeeId);
            command.Parameters.AddWithValue("@LeaveType", leaveRequest.LeaveType);
            command.Parameters.AddWithValue("@IsPaid", leaveRequest.IsPaid);
            command.Parameters.AddWithValue("@StartDate", leaveRequest.StartDate.Date);
            command.Parameters.AddWithValue("@EndDate", leaveRequest.EndDate.Date);
            command.Parameters.AddWithValue("@Reason", leaveRequest.Reason);
            command.Parameters.AddWithValue("@Status", leaveRequest.Status);
            command.Parameters.AddWithValue("@ReviewerNotes", string.IsNullOrWhiteSpace(leaveRequest.ReviewerNotes) ? DBNull.Value : leaveRequest.ReviewerNotes);
            command.Parameters.AddWithValue("@ReviewedBy", string.IsNullOrWhiteSpace(leaveRequest.ReviewedBy) ? DBNull.Value : leaveRequest.ReviewedBy);
            command.Parameters.AddWithValue("@ReviewedAt", leaveRequest.ReviewedAt.HasValue ? leaveRequest.ReviewedAt.Value : DBNull.Value);
        }

        private static string BuildScopeKey(int employeeId) =>
            $"employee:{employeeId}";

        private static string GetOfflineStoreLabel()
        {
            return DatabaseHelper.UsesSqlite(DatabaseConnectionTarget.Offline)
                ? "SQLite offline store"
                : "MySQL mirror";
        }

        private static bool GetOptionalBool(string envVar, bool defaultValue)
        {
            var raw = Environment.GetEnvironmentVariable(envVar);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return defaultValue;
            }

            return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(raw, "yes", StringComparison.OrdinalIgnoreCase);
        }

        private static int GetOptionalInt(string envVar, int defaultValue)
        {
            var raw = Environment.GetEnvironmentVariable(envVar);
            return int.TryParse(raw, out var parsed) ? parsed : defaultValue;
        }

        private sealed record PendingSyncOperation(long SyncQueueId, string EntityType, string OperationType, int RecordId, string ScopeKey);

        private sealed class LocalPayrollRow
        {
            public int PayrollId { get; init; }
            public int EmployeeId { get; init; }
            public DateTime PayPeriodStart { get; init; }
            public DateTime PayPeriodEnd { get; init; }
            public decimal RegularHours { get; init; }
            public decimal OvertimeHours { get; init; }
            public decimal GrossPay { get; init; }
            public decimal Deductions { get; init; }
            public decimal NetPay { get; init; }
            public decimal ManualDeduction { get; init; }
            public string ManualDeductionNote { get; init; } = string.Empty;
            public string Status { get; init; } = string.Empty;
            public DateTime CreatedAt { get; init; }
        }

        private sealed class LocalLeaveRequestRow
        {
            public int LeaveRequestId { get; init; }
            public int EmployeeId { get; init; }
            public string LeaveType { get; init; } = string.Empty;
            public bool IsPaid { get; init; }
            public DateTime StartDate { get; init; }
            public DateTime EndDate { get; init; }
            public string Reason { get; init; } = string.Empty;
            public string Status { get; init; } = string.Empty;
            public string ReviewerNotes { get; init; } = string.Empty;
            public string ReviewedBy { get; init; } = string.Empty;
            public DateTime? ReviewedAt { get; init; }
            public DateTime CreatedAt { get; init; }
            public DateTime UpdatedAt { get; init; }
        }
    }

    public sealed record OfflineSyncInitializationResult(
        bool IsReady,
        bool UsingOfflineDatabase,
        int PendingOperations,
        string Message);
}
