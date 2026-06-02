using System;
using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace AttendancePayrollSystem.DataAccess.DbCompat
{
    public sealed class MySqlCommand : IDisposable
    {
        private readonly MySqlConnection _connection;
        private readonly DbCommand _inner;
        private readonly MySqlParameterCollection _parameters;

        public MySqlCommand(string commandText, MySqlConnection connection)
            : this(commandText, connection, null)
        {
        }

        public MySqlCommand(string commandText, MySqlConnection connection, MySqlTransaction? transaction)
        {
            _connection = connection;
            _inner = connection.Inner.CreateCommand();
            _inner.CommandText = commandText;
            if (transaction != null)
            {
                _inner.Transaction = transaction.Inner;
            }

            _parameters = new MySqlParameterCollection(_inner.Parameters);
        }

        public MySqlParameterCollection Parameters => _parameters;

        public long LastInsertedId
        {
            get
            {
                if (_connection.Provider == DatabaseProvider.Sqlite)
                {
                    using var command = _connection.Inner.CreateCommand();
                    command.CommandText = "SELECT last_insert_rowid();";
                    return Convert.ToInt64(command.ExecuteScalar());
                }

                return _inner is MySqlConnector.MySqlCommand mySqlCommand
                    ? mySqlCommand.LastInsertedId
                    : 0L;
            }
        }

        public int ExecuteNonQuery()
        {
            return _inner.ExecuteNonQuery();
        }

        public object? ExecuteScalar()
        {
            return _inner.ExecuteScalar();
        }

        public MySqlDataReader ExecuteReader()
        {
            return new MySqlDataReader(_inner.ExecuteReader());
        }

        public void Dispose()
        {
            _inner.Dispose();
        }
    }
}
