using System;
using System.Collections.Generic;
using AttendancePayrollSystem.Models;
using AttendancePayrollSystem.Services;
using MySqlConnector;

namespace AttendancePayrollSystem.DataAccess
{
    public class LeaveDocumentRepository
    {
        public List<LeaveDocument> GetByLeaveRequestId(int leaveRequestId)
        {
            if (SupabaseConfig.UseApi)
                throw new NotSupportedException("Multi-file documents require local DB mode.");

            const string sql = @"
                SELECT DocumentId, LeaveRequestId, DocumentName, DocumentData, FileSizeBytes, UploadedAt
                FROM LeaveRequestDocuments
                WHERE LeaveRequestId = @LeaveRequestId
                ORDER BY UploadedAt ASC, DocumentId ASC";

            using var connection = DatabaseHelper.GetConnection();
            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@LeaveRequestId", leaveRequestId);
            connection.Open();
            using var reader = command.ExecuteReader();

            var docs = new List<LeaveDocument>();
            while (reader.Read())
                docs.Add(MapDocument(reader));
            return docs;
        }

        public int AddDocument(LeaveDocument doc)
        {
            if (SupabaseConfig.UseApi)
                throw new NotSupportedException("Multi-file documents require local DB mode.");

            const string sql = @"
                INSERT INTO LeaveRequestDocuments
                    (LeaveRequestId, DocumentName, DocumentData, FileSizeBytes, UploadedAt)
                VALUES
                    (@LeaveRequestId, @DocumentName, @DocumentData, @FileSizeBytes, @UploadedAt)";

            using var connection = DatabaseHelper.GetConnection();
            using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@LeaveRequestId", doc.LeaveRequestId);
            command.Parameters.AddWithValue("@DocumentName", doc.DocumentName);
            command.Parameters.AddWithValue("@DocumentData", doc.DocumentData);
            command.Parameters.AddWithValue("@FileSizeBytes", doc.FileSizeBytes);
            command.Parameters.AddWithValue("@UploadedAt", doc.UploadedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            connection.Open();
            command.ExecuteNonQuery();
            return Convert.ToInt32(command.LastInsertedId);
        }

        public void DeleteDocument(int documentId)
        {
            if (SupabaseConfig.UseApi)
                throw new NotSupportedException("Multi-file documents require local DB mode.");

            using var connection = DatabaseHelper.GetConnection();
            using var command = new MySqlCommand(
                "DELETE FROM LeaveRequestDocuments WHERE DocumentId = @DocumentId", connection);
            command.Parameters.AddWithValue("@DocumentId", documentId);
            connection.Open();
            command.ExecuteNonQuery();
        }

        public void DeleteAllForLeaveRequest(int leaveRequestId)
        {
            if (SupabaseConfig.UseApi)
                throw new NotSupportedException("Multi-file documents require local DB mode.");

            using var connection = DatabaseHelper.GetConnection();
            using var command = new MySqlCommand(
                "DELETE FROM LeaveRequestDocuments WHERE LeaveRequestId = @LeaveRequestId", connection);
            command.Parameters.AddWithValue("@LeaveRequestId", leaveRequestId);
            connection.Open();
            command.ExecuteNonQuery();
        }

        public int GetDocumentCount(int leaveRequestId)
        {
            if (SupabaseConfig.UseApi)
                return 0;

            using var connection = DatabaseHelper.GetConnection();
            using var command = new MySqlCommand(
                "SELECT COUNT(*) FROM LeaveRequestDocuments WHERE LeaveRequestId = @LeaveRequestId", connection);
            command.Parameters.AddWithValue("@LeaveRequestId", leaveRequestId);
            connection.Open();
            return Convert.ToInt32(command.ExecuteScalar());
        }

        private static LeaveDocument MapDocument(MySqlDataReader reader) => new()
        {
            DocumentId     = Convert.ToInt32(reader["DocumentId"]),
            LeaveRequestId = Convert.ToInt32(reader["LeaveRequestId"]),
            DocumentName   = Convert.ToString(reader["DocumentName"]) ?? string.Empty,
            DocumentData   = reader["DocumentData"] is DBNull ? Array.Empty<byte>() : (byte[])reader["DocumentData"],
            FileSizeBytes  = Convert.ToInt64(reader["FileSizeBytes"]),
            UploadedAt     = reader["UploadedAt"] is DBNull ? DateTime.MinValue : Convert.ToDateTime(reader["UploadedAt"])
        };
    }
}
