-- DEVELOPMENT ONLY
-- This grants broad table access to the anon role so the desktop app can use
-- a Supabase publishable key without Supabase Auth during the transition.
-- Do not use these policies in production.

drop policy if exists dev_anon_select_employees on public.employees;
drop policy if exists dev_anon_insert_employees on public.employees;
drop policy if exists dev_anon_update_employees on public.employees;
drop policy if exists dev_anon_delete_employees on public.employees;

drop policy if exists dev_anon_select_attendance on public.attendancerecords;
drop policy if exists dev_anon_insert_attendance on public.attendancerecords;
drop policy if exists dev_anon_update_attendance on public.attendancerecords;
drop policy if exists dev_anon_delete_attendance on public.attendancerecords;

drop policy if exists dev_anon_select_payroll on public.payrollrecords;
drop policy if exists dev_anon_insert_payroll on public.payrollrecords;
drop policy if exists dev_anon_update_payroll on public.payrollrecords;
drop policy if exists dev_anon_delete_payroll on public.payrollrecords;

drop policy if exists dev_anon_select_useraccounts on public.useraccounts;
drop policy if exists dev_anon_insert_useraccounts on public.useraccounts;
drop policy if exists dev_anon_update_useraccounts on public.useraccounts;
drop policy if exists dev_anon_delete_useraccounts on public.useraccounts;

create policy dev_anon_select_employees on public.employees for select to anon using (true);
create policy dev_anon_insert_employees on public.employees for insert to anon with check (true);
create policy dev_anon_update_employees on public.employees for update to anon using (true) with check (true);
create policy dev_anon_delete_employees on public.employees for delete to anon using (true);

create policy dev_anon_select_attendance on public.attendancerecords for select to anon using (true);
create policy dev_anon_insert_attendance on public.attendancerecords for insert to anon with check (true);
create policy dev_anon_update_attendance on public.attendancerecords for update to anon using (true) with check (true);
create policy dev_anon_delete_attendance on public.attendancerecords for delete to anon using (true);

create policy dev_anon_select_payroll on public.payrollrecords for select to anon using (true);
create policy dev_anon_insert_payroll on public.payrollrecords for insert to anon with check (true);
create policy dev_anon_update_payroll on public.payrollrecords for update to anon using (true) with check (true);
create policy dev_anon_delete_payroll on public.payrollrecords for delete to anon using (true);

create policy dev_anon_select_useraccounts on public.useraccounts for select to anon using (true);
create policy dev_anon_insert_useraccounts on public.useraccounts for insert to anon with check (true);
create policy dev_anon_update_useraccounts on public.useraccounts for update to anon using (true) with check (true);
create policy dev_anon_delete_useraccounts on public.useraccounts for delete to anon using (true);
