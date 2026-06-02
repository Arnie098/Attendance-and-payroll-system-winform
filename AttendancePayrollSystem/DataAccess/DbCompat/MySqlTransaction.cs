using System;
using System.Data.Common;

namespace AttendancePayrollSystem.DataAccess.DbCompat
{
    public sealed class MySqlTransaction : IDisposable
    {
        private readonly DbTransaction _inner;

        internal MySqlTransaction(DbTransaction inner, DatabaseProvider provider)
        {
            _inner = inner;
            Provider = provider;
        }

        public DatabaseProvider Provider { get; }

        internal DbTransaction Inner => _inner;

        public void Commit()
        {
            _inner.Commit();
        }

        public void Rollback()
        {
            _inner.Rollback();
        }

        public void Dispose()
        {
            _inner.Dispose();
        }
    }
}
