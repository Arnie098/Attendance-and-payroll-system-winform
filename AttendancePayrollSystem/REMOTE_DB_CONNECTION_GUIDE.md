# Supabase Setup Guide

> Note
> The app is now configured by default for direct Hostinger MySQL access through `.env` or `App.config`.
> This document remains only for the legacy Supabase API flow.

This project now supports two runtime modes:

- `SUPABASE_USE_API=true`: use the Supabase publishable key over `rest/v1`
- `SUPABASE_USE_API=false`: use a direct PostgreSQL connection with `Npgsql`

The current default is API mode.

## API mode with publishable key

This is the active migration path. It avoids direct database access from the desktop app, but it still requires RLS policies that allow the app to read and write its tables.

### 1. Run the schema

In the Supabase SQL Editor, run:

- `AttendancePayrollSystem/SUPABASE_SCHEMA.sql`

### 2. Run development policies

For the current desktop login flow, the app needs anon access to:

- `public.employees`
- `public.attendancerecords`
- `public.payrollrecords`
- `public.useraccounts`

For development, run:

- `AttendancePayrollSystem/SUPABASE_DEV_POLICIES.sql`

This is intentionally broad and is not production-safe.

### 3. Set environment values

`.env`

```env
SUPABASE_URL=https://umdjbkiqlkeyeetwkdzu.supabase.co
SUPABASE_PUBLISHABLE_KEY=sb_publishable_CQBzUHgsKZ3EfnATsiHxhw_ANANkzo5
SUPABASE_USE_API=true
ATTENDANCE_BOOTSTRAP_ADMIN_PASSWORD=YourStrongAdminPassword#2026
```

Optional demo account sync:

```env
ATTENDANCE_ENABLE_DEMO_ACCOUNTS=true
ATTENDANCE_DEMO_EMPLOYEE_PASSWORD=YourStrongDemoPassword#2026
```

### 4. Run the app

```powershell
dotnet run --project AttendancePayrollSystem\AttendancePayrollSystem.csproj
```

### Current limitation

This API mode still performs local username/password verification against the `useraccounts` table. That means the app needs access to password-hash data through RLS, which is acceptable only for development or a tightly controlled internal environment. A production-grade version should move authentication to Supabase Auth or a trusted backend.

## Direct PostgreSQL mode

If you need the old direct SQL path, set:

```env
SUPABASE_USE_API=false
ATTENDANCE_DB_CONNECTION=Host=db.<project-ref>.supabase.co;Port=5432;Database=postgres;Username=<db-user>;Password=<db-password>;SSL Mode=Require;Trust Server Certificate=true;
```

Use the exact connection string from `Project Settings > Database > Connection string`.

## Troubleshooting

- `Failed to initialize login accounts`
  - The schema exists, but anon write policies are still missing. Run `AttendancePayrollSystem/SUPABASE_DEV_POLICIES.sql`.
- `new row violates row-level security policy`
  - The publishable-key request reached Supabase, but the current role is not allowed to insert or update that table.
- `Missing ATTENDANCE_BOOTSTRAP_ADMIN_PASSWORD`
  - No admin account exists yet. Set a strong password and restart.
