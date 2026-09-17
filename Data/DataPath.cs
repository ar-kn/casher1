using System.IO;
using Microsoft.Data.Sqlite;

namespace PhoneAccounting.App.Data;

public static class DataPath
{
    public static string BaseFolder { get; private set; } = "";

    public static void Initialize()
    {
        BaseFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PhoneAccounting");

        Directory.CreateDirectory(BaseFolder);
    }

    public static string DatabaseFile => Path.Combine(BaseFolder, "phoneaccounting.db");

    public static string ConnectionString
    {
        get
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = DatabaseFile,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
                Pooling = true
            };
            return builder.ToString();
        }
    }
}
