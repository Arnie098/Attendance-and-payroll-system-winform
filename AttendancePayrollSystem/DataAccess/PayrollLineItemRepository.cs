using System;
using System.Collections.Generic;
using AttendancePayrollSystem.Models;
using MySqlConnector;

namespace AttendancePayrollSystem.DataAccess
{
    public class PayrollLineItemRepository
    {
        public List<PayrollLineItem> GetByPayrollId(int payrollId)
        {
            var items = new List<PayrollLineItem>();
            const string sql = @"
                SELECT Id, PayrollId, Label, Amount, ItemType, SortOrder
                FROM PayrollLineItems
                WHERE PayrollId = @PayrollId
                ORDER BY SortOrder, Id";

            using var connection = DatabaseHelper.GetConnection();
            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@PayrollId", payrollId);
            connection.Open();
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                items.Add(MapLineItem(reader));
            }

            return items;
        }

        public void SaveLineItems(int payrollId, List<PayrollLineItem> items)
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                using var deleteCommand = new MySqlCommand(
                    "DELETE FROM PayrollLineItems WHERE PayrollId = @PayrollId",
                    connection,
                    transaction);
                deleteCommand.Parameters.AddWithValue("@PayrollId", payrollId);
                deleteCommand.ExecuteNonQuery();

                for (var i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    const string insertSql = @"
                        INSERT INTO PayrollLineItems (PayrollId, Label, Amount, ItemType, SortOrder)
                        VALUES (@PayrollId, @Label, @Amount, @ItemType, @SortOrder)";

                    using var insertCommand = new MySqlCommand(insertSql, connection, transaction);
                    insertCommand.Parameters.AddWithValue("@PayrollId", payrollId);
                    insertCommand.Parameters.AddWithValue("@Label", item.Label.Trim());
                    insertCommand.Parameters.AddWithValue("@Amount", item.Amount);
                    insertCommand.Parameters.AddWithValue("@ItemType", (int)item.ItemType);
                    insertCommand.Parameters.AddWithValue("@SortOrder", i);
                    insertCommand.ExecuteNonQuery();
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        private static PayrollLineItem MapLineItem(MySqlDataReader reader)
        {
            return new PayrollLineItem
            {
                Id = Convert.ToInt32(reader["Id"]),
                PayrollId = Convert.ToInt32(reader["PayrollId"]),
                Label = Convert.ToString(reader["Label"]) ?? string.Empty,
                Amount = Convert.ToDecimal(reader["Amount"]),
                ItemType = (PayrollLineItemType)Convert.ToInt32(reader["ItemType"]),
                SortOrder = Convert.ToInt32(reader["SortOrder"])
            };
        }
    }
}
