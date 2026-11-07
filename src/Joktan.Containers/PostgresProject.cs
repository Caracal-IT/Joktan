using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Joktan.Containers.Resources;
using Joktan.Core;

namespace Joktan.Containers;

/// <summary>
/// Joktan's own compose project for the PostgreSQL servers made on Data / Database: a compose file
/// with a service for each, and a folder for each with an environment file holding its password,
/// readable only by the user. Each server's data is in its own folder on this computer, mounted into
/// the container, so it outlives the container. Everything but the data is written from the
/// databases' settings, and written again when they change or one is started.
/// </summary>
public static class PostgresProject
{
    public const string ComposeFileName = "docker-compose.yml";
    public const string DefaultName = "joktan-postgres";
    public const string DefaultImage = "docker.io/library/postgres:18";

    /// <summary>Where the data folder is mounted in the container.</summary>
    public const string ContainerMount = "/var/lib/postgresql/joktan";

    /// <summary>
    /// Where the files are kept, in the folder mounted. A folder below the mount, as PostgreSQL
    /// only sets up an empty folder, and the system may leave files in the one chosen.
    /// </summary>
    public const string ContainerData = ContainerMount + "/data";

    private const string EnvironmentFile = "postgres.env";
    private const string Marker = ProjectFiles.Marker;

    /// <summary>Where the project is written when no folder is chosen: <c>containers/postgres</c> in Joktan's data folder.</summary>
    public static string DefaultFolder => Path.Combine(DataFolder.For("containers"), "postgres");

    /// <summary>Where new databases' data folders go when no folder is chosen: <c>databases</c> in Joktan's data folder.</summary>
    public static string DefaultDataRoot => DataFolder.For("databases");

    public static string ComposeFileIn(string folder) => Path.Combine(folder, ComposeFileName);

    /// <summary>Where a database's data goes by default: a folder named after it in <paramref name="root"/>.</summary>
    public static string DataFolderFor(string root, string name) => Path.Combine(root, name.Trim());

    /// <summary>Whether PostgreSQL has set up a database in the data folder, after which its user, password and database are kept as they are.</summary>
    public static bool HasData(string dataFolder) => File.Exists(Path.Combine(dataFolder, "data", "PG_VERSION"));

    /// <summary>
    /// Writes the project to <paramref name="folder"/>, makes each database's data folder, and
    /// removes the folders of databases no longer in it, but never a data folder. Files whose content
    /// is unchanged are left alone, so that starting an unchanged database does not recreate its
    /// container. Every failure is reported as a <see cref="FormatException"/> whose message can be
    /// shown as it is.
    /// </summary>
    /// <param name="passwordOf">The password of a database.</param>
    public static void Write(string folder, string project, string image, IReadOnlyList<PostgresDatabase> databases,
        Func<PostgresDatabase, string?> passwordOf)
    {
        if ((MosquittoProject.CheckProjectName(project) ?? MosquittoProject.CheckImage(image) ?? MosquittoProject.CheckFolder(folder)) is { } settingsError)
            throw new FormatException(settingsError);
        foreach (var database in databases)
        {
            var password = passwordOf(database);
            if ((database.Check(databases, password is { Length: > 0 }) ?? PostgresDatabase.CheckPassword(password ?? "")) is { } problem)
                throw new FormatException(string.Format(Strings.PostgresProject_DatabaseProblem, database.Name, problem));
        }

        try
        {
            Directory.CreateDirectory(folder);
            foreach (var database in databases)
            {
                var own = Path.Combine(folder, database.Name);
                ProjectFiles.CreatePrivateFolder(own);
                ProjectFiles.WritePrivate(Path.Combine(own, EnvironmentFile), EnvironmentText(database, passwordOf(database) ?? ""), onlyIfChanged: true);
            }

            var names = databases.Select(d => d.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var old in Directory.GetDirectories(folder).Where(d => !names.Contains(Path.GetFileName(d))))
                DeleteDatabase(old);

            ProjectFiles.WriteIfChanged(ComposeFileIn(folder), ComposeText(project.Trim(), image.Trim(), databases));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new FormatException(string.Format(Strings.PostgresProject_CannotBeWrittenTo, folder, e.Message), e);
        }

        foreach (var database in databases)
        {
            try
            {
                Directory.CreateDirectory(database.DataFolder.Trim());
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                throw new FormatException(string.Format(Strings.PostgresProject_TheDataFolderCannotBeMade, database.DataFolder, e.Message), e);
            }
        }
    }

    /// <summary>Deletes what <see cref="Write"/> wrote to the folder, and the folders once empty. Data folders and anything else are left.</summary>
    public static void Delete(string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
                return;
            foreach (var database in Directory.GetDirectories(folder))
                DeleteDatabase(database);
            ProjectFiles.DeleteIfOurs(ComposeFileIn(folder));
            ProjectFiles.DeleteIfEmpty(folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new FormatException(string.Format(Strings.PostgresProject_CannotBeWrittenTo, folder, e.Message), e);
        }
    }

    public static string ComposeText(string project, string image, IEnumerable<PostgresDatabase> databases)
    {
        var text = new StringBuilder();
        text.Append(Marker).Append(" from the databases on Data / Database. Change them there: what is\n")
            .Append("# changed here is replaced the next time a database is saved or started.\n")
            .Append("name: ").Append(project).Append("\n\n");

        var list = databases.ToList();
        if (list.Count == 0)
            return text.Append("services: {}\n").ToString();

        text.Append("services:\n");
        foreach (var database in list)
        {
            text.Append("  ").Append(database.Name).Append(":\n")
                .Append("    image: ").Append(image).Append('\n')
                .Append("    restart: unless-stopped\n")
                .Append("    # The password is in this file, which only you can read.\n")
                .Append("    env_file: ./").Append(database.Name).Append('/').Append(EnvironmentFile).Append('\n')
                .Append("    environment:\n")
                .Append("      POSTGRES_USER: ").Append(database.Username).Append('\n')
                .Append("      POSTGRES_DB: ").Append(database.Database).Append('\n')
                .Append("      PGDATA: ").Append(ContainerData).Append('\n')
                .Append("    ports:\n")
                .Append(CultureInfo.InvariantCulture, $"      - \"{database.Port}:{PostgresDatabase.ContainerPort}\"\n")
                .Append("    volumes:\n")
                .Append("      - type: bind\n")
                .Append("        source: ").Append(Quoted(database.DataFolder.Trim())).Append('\n')
                .Append("        target: ").Append(ContainerMount).Append('\n')
                .Append("    healthcheck:\n")
                .Append("      test: [\"CMD\", \"pg_isready\", \"-U\", \"").Append(database.Username).Append("\", \"-d\", \"").Append(database.Database).Append("\"]\n")
                .Append("      interval: 10s\n")
                .Append("      timeout: 5s\n")
                .Append("      retries: 5\n")
                .Append('\n');
        }

        return text.ToString().TrimEnd('\n') + "\n";
    }

    /// <summary>The environment file, which holds only the password, quoted so that compose reads it as it is.</summary>
    public static string EnvironmentText(PostgresDatabase database, string password)
    {
        var quoted = password.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("$", "\\$");
        return $"{Marker} for the database {database.Name}. Change it on Data / Database.\nPOSTGRES_PASSWORD=\"{quoted}\"\n";
    }

    // A path as a YAML string in double quotes, with compose's $ doubled so it is not read as a variable.
    private static string Quoted(string path)
        => "\"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("$", "$$") + "\"";

    // Only a folder whose environment file Joktan wrote is a database's, so nothing else is touched.
    private static void DeleteDatabase(string folder)
    {
        var file = Path.Combine(folder, EnvironmentFile);
        if (!ProjectFiles.IsOurs(file))
            return;
        File.Delete(file);
        ProjectFiles.DeleteIfEmpty(folder);
    }
}
