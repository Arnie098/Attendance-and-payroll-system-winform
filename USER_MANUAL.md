# Attendance Payroll System User Manual

Last updated: March 24, 2026

## 1. Purpose

This manual provides the operating guide for configuring and using the Attendance Payroll System in a deployment where employee records come from the school database, while attendance and payroll records are maintained in the attendance database.

## 2. Data Ownership Policy

Use the following data ownership model for this project:

- `School database` is the only source of truth for employee master records.
- `Attendance database` is the operating database for attendance, payroll, backups, and application runtime data.
- `Offline database` stores the merged working schema required for local operation, while preserving the school database as the source of truth for employee records.

Apply the following rules during deployment and daily operation:

- Create, update, and remove employee records in the school management database.
- Use the attendance database for attendance records, payroll records, and backup exports.
- Keep the offline database aligned with the merged operational schema used by the desktop application.
- Resolve employee identity conflicts in favor of the school database.
- Resolve attendance and payroll conflicts in favor of the attendance database.

## 3. Configuration

### 3.1 Configuration sources

The application reads settings from these locations:

1. Machine environment variables
2. Laptop-specific database override file created from `Database Settings`
3. Repository `.env`
4. `App.config`

Laptop-specific overrides are stored at:

- `%LocalAppData%\AttendancePayrollSystem\database.override.env`

Use [.env.example](.env.example) as the template for deployment values.

### 3.2 Required environment values

| Setting | Use |
| --- | --- |
| `ATTENDANCE_DB_CONNECTION` | Main attendance database connection |
| `SCHOOL_DB_CONNECTION` | School management database connection |
| `ATTENDANCE_OFFLINE_DB_CONNECTION` | Local offline working database connection |
| `ATTENDANCE_BOOTSTRAP_ADMIN_PASSWORD` | Initial administrator password |
| `ATTENDANCE_SCHOOL_EMPLOYEES_ONLY=true` | Enforces school database ownership for employees |

Recommended supporting values:

| Setting | Recommended value |
| --- | --- |
| `ATTENDANCE_OFFLINE_AUTO_SYNC` | `true` |
| `ATTENDANCE_OFFLINE_SYNC_INTERVAL_SECONDS` | `15` or higher |
| `ATTENDANCE_ENABLE_DEMO_ACCOUNTS` | `false` |
| `SUPABASE_USE_API` | `false` |

### 3.3 Database targeting notes

Use `Database Settings` from the login screen only for the main attendance database connection used by the current laptop.

Keep these connections managed separately in the deployment configuration:

- school database connection
- offline database connection

## 4. First-Time Setup

Complete the following steps on each deployment:

1. Prepare the attendance database.
2. Prepare the school management database.
3. Prepare the offline local database.
4. Update `.env` with the required connection strings.
5. Set `ATTENDANCE_SCHOOL_EMPLOYEES_ONLY=true`.
6. Set the administrator bootstrap password.
7. Start the application.
8. Sign in as `admin`.
9. Open `Employee Management`.
10. Use `Refresh` to load employees from the school database into the attendance-side working data.
11. Confirm that attendance and payroll operations use the attendance database.
12. Confirm that the offline database receives the merged working schema and sync data.

## 5. Administrator Sign-In

Use these credentials at first deployment:

- Username: `admin`
- Password: value from `ATTENDANCE_BOOTSTRAP_ADMIN_PASSWORD`

After sign-in, confirm the database target shown on the login screen before continuing with daily work.

## 6. Employee Synchronization Procedure

Employee records must be synchronized from the school database before normal attendance and payroll processing.

Daily procedure:

1. Sign in as administrator.
2. Open `Employee Management`.
3. Click `Refresh`.
4. Confirm the employee list reflects the current school database records.
5. Continue attendance and payroll processing only after employee synchronization is complete.

Use this same procedure after:

- newly hired employees are added to the school system
- employee profile changes are made in the school system
- employee status changes are made in the school system

## 7. Attendance Procedure

Attendance processing is maintained in the attendance database.

Administrator procedure:

1. Open `Employee Management`.
2. Select the employee.
3. Click `Open Attendance`.
4. Review the daily attendance summary.
5. Record or correct attendance as needed.
6. Save the updated attendance data.

Employee procedure:

1. Sign in using the assigned employee account.
2. Open `Attendance`.
3. Complete the daily time in or time out process.
4. Review the attendance summary before closing the window.

## 8. Payroll Procedure

Payroll processing is maintained in the attendance database.

Administrator procedure:

1. Open `Employee Management`.
2. Select the employee.
3. Click `Open Payroll Modal`.
4. Select the payroll period.
5. Run payroll processing for the selected period.
6. Review the computed values.
7. Update payroll status according to the payroll cycle.
8. Save the payroll record.

Repeat this process for each employee included in the payroll run.

## 9. Backup Procedure

Backups are generated from the attendance database.

Procedure:

1. Sign in as administrator.
2. Open the `Dashboard`.
3. Click `Backup Database`.
4. Select the backup type required by your backup schedule.
5. Choose the destination folder.
6. Save the backup file.
7. Keep the generated `.manifest.json` file in the same folder as the backup SQL file.

For scheduled backup organization:

- use one folder per database source
- keep backup files and manifest files together
- keep backup folders clearly labeled by server and database name

## 10. Offline Operation

The offline database should carry the merged working schema required by the attendance application.

This local working schema should include:

- school-synchronized employee reference data
- user account data required for sign-in
- attendance records
- payroll records
- application support tables required for synchronization

Daily operating guidance:

1. Keep online and offline database connections configured on each laptop.
2. Allow the application to complete an initial online synchronization before relying on local offline work.
3. When connectivity is interrupted, continue working against the offline database.
4. When connectivity returns, allow synchronization to complete before closing the application.
5. Review employee data after reconnecting to confirm school-sourced records remain aligned.

## 11. Daily Operations Checklist

Start of day:

1. Open the application.
2. Verify the displayed database target.
3. Sign in as administrator.
4. Refresh employee synchronization from the school database.
5. Confirm attendance processing is ready.

During the day:

1. Process attendance transactions.
2. Review employee attendance records when needed.
3. Keep payroll entries within the attendance database workflow.

End of day:

1. Review attendance completion.
2. Finalize payroll updates if scheduled for that cycle.
3. Export the database backup.
4. Confirm synchronization status before shutting down the workstation.

## 12. Troubleshooting

### Sign-in screen is not ready

Check the following:

1. Attendance database connection
2. Laptop-specific database override
3. `.env` values
4. Online database availability
5. Offline database availability

### Employee record is missing

Use this order:

1. Verify the employee exists in the school database.
2. Refresh employee synchronization from `Employee Management`.
3. Review the school database connection values.

### Offline workstation is not synchronizing correctly

Check the following:

1. Offline database connection string
2. Sync interval settings
3. Local MySQL service availability
4. Online attendance database connectivity

### Backup chain is incomplete

Check the following:

1. Backup SQL file exists
2. Matching `.manifest.json` file exists in the same folder
3. Backup folder matches the intended server and database

## 13. Repository References

For deployment support, use:

- [.env.example](.env.example)
- [AttendancePayrollSystem/REMOTE_DB_CONNECTION_GUIDE.md](AttendancePayrollSystem/REMOTE_DB_CONNECTION_GUIDE.md)
- [Installer/README.md](Installer/README.md)
