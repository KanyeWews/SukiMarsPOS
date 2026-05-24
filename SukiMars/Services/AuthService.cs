using Microsoft.Data.SqlClient;

namespace SukiMars.Services;

public sealed class AuthService
{
    public async Task<UserSession?> AuthenticateAsync(string username, string password)
    {
        const string query = """
            SELECT userID, username, userRole, status
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
            Status = reader.GetString(3)
        };
    }

    public async Task CreateAccountAsync(string username, string password, string role)
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
                '',
                '',
                @username,
                @password,
                '',
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
        insert.Parameters.AddWithValue("@username", username);
        insert.Parameters.AddWithValue("@password", password);
        insert.Parameters.AddWithValue("@role", role);
        await insert.ExecuteNonQueryAsync();
    }
}
