using System;
using System.Data.Common;

namespace AttendancePayrollSystem.DataAccess.DbCompat
{
    public sealed class MySqlDataReader : IDisposable
    {
        private readonly DbDataReader _inner;

        internal MySqlDataReader(DbDataReader inner)
        {
            _inner = inner;
        }

        public int FieldCount => _inner.FieldCount;

        public object this[string name] => _inner[name];

        public object this[int ordinal] => _inner[ordinal];

        public string GetName(int ordinal)
        {
            return _inner.GetName(ordinal);
        }

        public object GetValue(int ordinal)
        {
            return _inner.GetValue(ordinal);
        }

        public bool Read()
        {
            return _inner.Read();
        }

        public void Close()
        {
            _inner.Close();
        }

        public void Dispose()
        {
            _inner.Dispose();
        }
    }
}
