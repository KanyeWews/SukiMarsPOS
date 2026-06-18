using Microsoft.Data.SqlClient;

namespace SukiMars.Services;

public sealed class AuthService
{
    public async Task<UserSession?> AuthenticateAsync(string username, string password)
    {
        const string query = """
            SELECT userID, username, userRole, status, ISNULL(firstName, ''), ISNULL(lastName, '')
            FROM user_accounts
            WHERE username = @username AND password = @password
            """;

        await using SqlConnection connection = new(DatabaseConfig.ConnectionString);
        await connection.OpenAsync();

        await using SqlCommand command = new(query, connection);
        command.Parameters.AddWithValue("@username", username);
        command.Parameters.AddWithValue("@password", password);

        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new UserSession
        {
            UserId = reader.GetInt32(0),
            Username = reader.GetString(1),
            Role = reader.GetString(2),
            Status = reader.GetString(3),
            FirstName = reader.GetString(4),
            LastName = reader.GetString(5)
        };
    }

    public async Task CreateAccountAsync(string username, string password, string role, string firstName, string lastName, string pincode)
    {
        const string existsQuery = """
            SELECT COUNT(1)
            FROM dbo.user_accounts
            WHERE username = @username
            """;

        const string insertQuery = """
            INSERT INTO dbo.user_accounts
            (
                firstName,
                lastName,
                username,
                [password],
                pincode,
                userRole,
                [status]
            )
            VALUES
            (
                @firstName,
                @lastName,
                @username,
                @password,
                @pincode,
                @role,
                'Active'
            )
            """;

        await using SqlConnection connection = new(DatabaseConfig.ConnectionString);
        await connection.OpenAsync();

        await using (SqlCommand exists = new(existsQuery, connection))
        {
            exists.Parameters.AddWithValue("@username", username);
            int count = (int)(await exists.ExecuteScalarAsync() ?? 0);
            if (count > 0)
            {
                throw new InvalidOperationException("Username already exists.");
            }
        }

        await using SqlCommand insert = new(insertQuery, connection);
        insert.Parameters.AddWithValue("@firstName", (object?)firstName ?? DBNull.Value);
        insert.Parameters.AddWithValue("@lastName", (object?)lastName ?? DBNull.Value);
        insert.Parameters.AddWithValue("@username", username);
        insert.Parameters.AddWithValue("@password", password);
        insert.Parameters.AddWithValue("@pincode", string.IsNullOrWhiteSpace(pincode) ? (object)DBNull.Value : pincode);
        insert.Parameters.AddWithValue("@role", role);
        await insert.ExecuteNonQueryAsync();
    }

    // New: user DTO
    public sealed record UserAccount(int UserId, string FirstName, string LastName, string Username, string Role, string Status);

    // New: returns all users
    public async Task<List<UserAccount>> GetAllUsersAsync()
    {
        const string sql = """
            SELECT userID,
                   ISNULL(firstName, '') AS firstName,
                   ISNULL(lastName, '') AS lastName,
                   ISNULL(username, '') AS username,
                   ISNULL(userRole, '') AS role,
                   ISNULL([status], '') AS status
            FROM dbo.user_accounts
            ORDER BY username ASC
            """;

        var list = new List<UserAccount>();
        await using SqlConnection conn = new(DatabaseConfig.ConnectionString);
        await conn.OpenAsync();
        await using SqlCommand cmd = new(sql, conn);
        await using SqlDataReader r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            int id = r.IsDBNull(0) ? 0 : r.GetInt32(0);
            string first = r.IsDBNull(1) ? string.Empty : r.GetString(1);
            string last = r.IsDBNull(2) ? string.Empty : r.GetString(2);
            string user = r.IsDBNull(3) ? string.Empty : r.GetString(3);
            string role = r.IsDBNull(4) ? string.Empty : r.GetString(4);
            string status = r.IsDBNull(5) ? string.Empty : r.GetString(5);
            list.Add(new UserAccount(id, first, last, user, role, status));
        }

        return list;
    }

    // New: toggle/update user status
    public async Task UpdateUserStatusAsync(int userId, string status)
    {
        const string sql = """
            UPDATE dbo.user_accounts
            SET [status] = @status
            WHERE userID = @userId
            """;

        await using SqlConnection conn = new(DatabaseConfig.ConnectionString);
        await conn.OpenAsync();
        await using SqlCommand cmd = new(sql, conn);
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@userId", userId);
        await cmd.ExecuteNonQueryAsync();
    }

    public sealed record UserAccountDetails(int UserId, string FirstName, string LastName, string Username, string Role, string Status, string? Pincode);

    public async Task<UserAccountDetails?> GetUserByIdAsync(int userId)
    {
        const string sql = """
            SELECT userID,
                   ISNULL(firstName,'') AS firstName,
                   ISNULL(lastName,'') AS lastName,
                   ISNULL(username,'') AS username,
                   ISNULL(userRole,'') AS role,
                   ISNULL([status],'') AS status,
                   pincode
            FROM dbo.user_accounts
            WHERE userID = @userId
            """;

        await using SqlConnection conn = new(DatabaseConfig.ConnectionString);
        await conn.OpenAsync();
        await using SqlCommand cmd = new(sql, conn);
        cmd.Parameters.AddWithValue("@userId", userId);
        await using SqlDataReader r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;

        int id = r.IsDBNull(0) ? 0 : r.GetInt32(0);
        string first = r.IsDBNull(1) ? string.Empty : r.GetString(1);
        string last = r.IsDBNull(2) ? string.Empty : r.GetString(2);
        string user = r.IsDBNull(3) ? string.Empty : r.GetString(3);
        string role = r.IsDBNull(4) ? string.Empty : r.GetString(4);
        string status = r.IsDBNull(5) ? string.Empty : r.GetString(5);
        string? pincode = r.IsDBNull(6) ? null : r.GetString(6);

        return new UserAccountDetails(id, first, last, user, role, status, pincode);
    }

    public async Task UpdateUserAsync(int userId, string firstName, string lastName, string role, string? pincode, string? newPassword = null)
    {
        // if newPassword is provided, update password as well, otherwise leave it unchanged
        string sql;
        if (string.IsNullOrEmpty(newPassword))
        {
            sql = """
                UPDATE dbo.user_accounts
                SET firstName = @firstName,
                    lastName = @lastName,
                    userRole = @role,
                    pincode = @pincode
                WHERE userID = @userId
                """;
        }
        else
        {
            sql = """
                UPDATE dbo.user_accounts
                SET firstName = @firstName,
                    lastName = @lastName,
                    userRole = @role,
                    pincode = @pincode,
                    [password] = @newPassword
                WHERE userID = @userId
                """;
        }

        await using SqlConnection conn = new(DatabaseConfig.ConnectionString);
        await conn.OpenAsync();
        await using SqlCommand cmd = new(sql, conn);
        cmd.Parameters.AddWithValue("@firstName", (object?)firstName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@lastName", (object?)lastName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@role", (object?)role ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@pincode", string.IsNullOrWhiteSpace(pincode) ? (object)DBNull.Value : pincode);
        cmd.Parameters.AddWithValue("@userId", userId);
        if (!string.IsNullOrEmpty(newPassword))
        {
            cmd.Parameters.AddWithValue("@newPassword", newPassword);
        }
        await cmd.ExecuteNonQueryAsync();
    }
}
