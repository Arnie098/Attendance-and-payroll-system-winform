using System;
using System.Data.Common;

namespace AttendancePayrollSystem.DataAccess.DbCompat
{
    public sealed class MySqlParameterCollection
    {
        private readonly DbParameterCollection _inner;

        internal MySqlParameterCollection(DbParameterCollection inner)
        {
            _inner = inner;
        }

        public void AddWithValue(string parameterName, object? value)
        {
            var parameter = CreateParameter(parameterName, value);
            _inner.Add(parameter);
        }

        private DbParameter CreateParameter(string parameterName, object? value)
        {
            DbParameter parameter;
            if (_inner is Microsoft.Data.Sqlite.SqliteParameterCollection)
            {
                parameter = new Microsoft.Data.Sqlite.SqliteParameter();
            }
            else
            {
                parameter = new MySqlConnector.MySqlParameter();
            }

            parameter.ParameterName = parameterName;
            parameter.Value = value ?? DBNull.Value;
            return parameter;
        }
    }
}
