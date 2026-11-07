using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Joktan.Certificates;
using Joktan.Containers.Resources;
using Joktan.Core;

namespace Joktan.Containers;

/// <summary>
/// Joktan's own compose project for the Mosquitto brokers made on Data / MQTT: a compose
/// file with a service for each broker, and a folder for each with its Dockerfile, configuration,
/// password file and certificates. They are copied into the image rather than mounted, because
/// Podman on macOS only shares some folders with its virtual machine. Everything here is written
/// from the brokers' settings, and written again when they change or a broker is started.
/// </summary>
public static class MosquittoProject
{
    public const string ComposeFileName = "docker-compose.yml";
    public const string DefaultName = "joktan-mosquitto";
    public const string DefaultImage = "docker.io/library/eclipse-mosquitto:2";

    private const string ConfigFile = "mosquitto.conf";
    private const string PasswordFile = "passwd";
    private const string Dockerfile = "Dockerfile";
    private const string CertificatesFolder = "certs";
    private const string CertificateFile = "cert.pem";
    private const string KeyFile = "key.pem";
    private const string AuthorityFile = "ca.pem";

    // The first line of every file written here, so that only those are ever replaced or deleted.
    private const string Marker = "# Written by Joktan";

    /// <summary>Where the project is written when no folder is chosen: <c>containers/mosquitto</c> in Joktan's data folder.</summary>
    public static string DefaultFolder => Path.Combine(DataFolder.For("containers"), "mosquitto");

    public static string ComposeFileIn(string folder) => Path.Combine(folder, ComposeFileName);

    /// <summary>What is wrong with a compose project name, or null: lower-case letters, digits, dashes and underscores.</summary>
    public static string? CheckProjectName(string name)
    {
        name = name.Trim();
        if (name.Length == 0)
            return Strings.MosquittoProject_EnterAProjectName;
        if ((!char.IsAsciiLetterLower(name[0]) && !char.IsAsciiDigit(name[0]))
            || name.Any(c => !char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c) && c is not ('-' or '_')))
            return Strings.MosquittoProject_UseLowerCaseLettersDigits;
        return null;
    }

    /// <summary>What is wrong with the image the brokers are built from, or null.</summary>
    public static string? CheckImage(string image)
    {
        image = image.Trim();
        if (image.Length == 0)
            return Strings.MosquittoProject_EnterAnImage;
        if (image.Any(char.IsWhiteSpace))
            return Strings.MosquittoProject_AnImageNameHasNoSpaces;
        return null;
    }

    /// <summary>What is wrong with the folder the project is written to, or null. Empty is the default folder.</summary>
    public static string? CheckFolder(string folder)
    {
        folder = folder.Trim();
        if (folder.Length == 0)
            return null;
        if (folder.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || !Path.IsPathFullyQualified(folder))
            return Strings.MosquittoProject_EnterTheFolderSFullPath;
        if (File.Exists(folder))
            return string.Format(Strings.MosquittoProject_IsAFileNotAFolder, folder);
        return null;
    }

    /// <summary>
    /// Writes the project to <paramref name="folder"/>, and removes the folders of brokers no longer
    /// in it. Files whose content is unchanged are left alone, and a password file is only written
    /// again when its password has changed, so that an unchanged broker's image stays the same and
    /// starting it does not recreate its container. Every failure is reported as a
    /// <see cref="FormatException"/> whose message can be shown as it is.
    /// </summary>
    /// <param name="passwordOf">The password of a broker that has a login.</param>
    public static void Write(string folder, string project, string image, IReadOnlyList<MosquittoBroker> brokers,
        Func<MosquittoBroker, string?> passwordOf, CertificateStore certificates)
    {
        if ((CheckProjectName(project) ?? CheckImage(image) ?? CheckFolder(folder)) is { } settingsError)
            throw new FormatException(settingsError);
        foreach (var broker in brokers)
        {
            if (broker.Check(brokers, certificates, passwordOf(broker) is { Length: > 0 }) is { } problem)
                throw new FormatException(string.Format(Strings.MosquittoProject_BrokerProblem, broker.Name, problem));
        }

        try
        {
            Directory.CreateDirectory(folder);
            foreach (var broker in brokers)
                WriteBroker(Path.Combine(folder, broker.Name), image.Trim(), broker, passwordOf(broker) ?? "", certificates);

            var names = brokers.Select(b => b.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var old in Directory.GetDirectories(folder).Where(d => !names.Contains(Path.GetFileName(d))))
                DeleteBroker(old);

            WriteIfChanged(ComposeFileIn(folder), ComposeText(project.Trim(), brokers));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new FormatException(string.Format(Strings.MosquittoProject_CannotBeWrittenTo, folder, e.Message), e);
        }
    }

    /// <summary>Deletes what <see cref="Write"/> wrote to the folder, and the folders once empty. Anything else is left.</summary>
    public static void Delete(string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
                return;
            foreach (var broker in Directory.GetDirectories(folder))
                DeleteBroker(broker);
            DeleteIfOurs(ComposeFileIn(folder));
            DeleteIfEmpty(folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new FormatException(string.Format(Strings.MosquittoProject_CannotBeWrittenTo, folder, e.Message), e);
        }
    }

    public static string ComposeText(string project, IEnumerable<MosquittoBroker> brokers)
    {
        var text = new StringBuilder();
        text.Append(Marker).Append(" from the brokers on Data / MQTT. Change them there: what is\n")
            .Append("# changed here is replaced the next time a broker is saved or started.\n")
            .Append("name: ").Append(project).Append("\n\n");

        var list = brokers.ToList();
        if (list.Count == 0)
            return text.Append("services: {}\n").ToString();

        text.Append("services:\n");
        foreach (var broker in list)
        {
            text.Append("  ").Append(broker.Name).Append(":\n")
                .Append("    build: ./").Append(broker.Name).Append('\n')
                .Append("    pull_policy: build\n")
                .Append("    image: localhost/").Append(project).Append('-').Append(broker.Name).Append(":latest\n")
                .Append("    restart: unless-stopped\n")
                .Append("    ports:\n");
            Port(broker.Port, MosquittoBroker.ContainerPort, "plain MQTT");
            Port(broker.TlsPort, MosquittoBroker.ContainerTlsPort, "TLS");
            Port(broker.MutualTlsPort, MosquittoBroker.ContainerMutualTlsPort, "mutual TLS");

            // The check subscribes on the plain port, which it can only do when no login is needed.
            if (broker.Port is not null && !broker.HasLogin)
            {
                text.Append("    healthcheck:\n")
                    .Append("      test: [\"CMD\", \"mosquitto_sub\", \"-h\", \"localhost\", \"-p\", \"")
                    .Append(MosquittoBroker.ContainerPort).Append("\", \"-t\", \"$$SYS/broker/uptime\", \"-C\", \"1\", \"-W\", \"3\"]\n")
                    .Append("      interval: 10s\n")
                    .Append("      timeout: 5s\n")
                    .Append("      retries: 3\n");
            }

            text.Append('\n');

            void Port(int? host, int container, string what)
            {
                if (host is { } port)
                    text.Append(CultureInfo.InvariantCulture, $"      - \"{port}:{container}\" # {what}\n");
            }
        }

        return text.ToString().TrimEnd('\n') + "\n";
    }

    public static string ConfigText(MosquittoBroker broker)
    {
        var text = new StringBuilder();
        text.Append(Marker).Append(" for the broker ").Append(broker.Name).Append(". Change it on Data / MQTT.\n")
            .Append("log_dest stdout\n");
        if (broker.HasLogin)
            text.Append("allow_anonymous false\npassword_file /mosquitto/config/").Append(PasswordFile).Append('\n');
        else
            text.Append("allow_anonymous true\n");

        if (broker.Port is { } port)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n# Plain MQTT, published on {port}\n")
                .Append(CultureInfo.InvariantCulture, $"listener {MosquittoBroker.ContainerPort}\n");
        }

        if (broker.TlsPort is { } tls)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n# TLS, published on {tls}\n")
                .Append(CultureInfo.InvariantCulture, $"listener {MosquittoBroker.ContainerTlsPort}\n")
                .Append("certfile /mosquitto/certs/").Append(CertificateFile).Append('\n')
                .Append("keyfile /mosquitto/certs/").Append(KeyFile).Append('\n');
        }

        if (broker.MutualTlsPort is { } mutual)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n# Mutual TLS, published on {mutual}: clients need a certificate signed by the CA\n")
                .Append(CultureInfo.InvariantCulture, $"listener {MosquittoBroker.ContainerMutualTlsPort}\n")
                .Append("cafile /mosquitto/certs/").Append(AuthorityFile).Append('\n')
                .Append("certfile /mosquitto/certs/").Append(CertificateFile).Append('\n')
                .Append("keyfile /mosquitto/certs/").Append(KeyFile).Append('\n')
                .Append("require_certificate true\n");
        }

        return text.ToString();
    }

    public static string DockerfileText(string image, MosquittoBroker broker)
    {
        var text = new StringBuilder();
        text.Append(Marker).Append(" for the broker ").Append(broker.Name).Append(". Change it on Data / MQTT.\n")
            .Append("FROM ").Append(image).Append('\n')
            .Append("COPY --chown=mosquitto:mosquitto ").Append(ConfigFile).Append(" /mosquitto/config/").Append(ConfigFile).Append('\n');
        var privateFiles = new List<string>();
        if (broker.HasLogin)
        {
            text.Append("COPY --chown=mosquitto:mosquitto ").Append(PasswordFile).Append(" /mosquitto/config/").Append(PasswordFile).Append('\n');
            privateFiles.Add("/mosquitto/config/" + PasswordFile);
        }

        if (broker.UsesTls)
        {
            text.Append("COPY --chown=mosquitto:mosquitto ").Append(CertificatesFolder).Append("/ /mosquitto/certs/\n");
            privateFiles.Add("/mosquitto/certs/" + KeyFile);
        }

        // Mosquitto warns about a password file others can read, and the key is as private.
        if (privateFiles.Count > 0)
            text.Append("RUN chmod 0600 ").Append(string.Join(' ', privateFiles)).Append('\n');
        return text.ToString();
    }

    private static void WriteBroker(string folder, string image, MosquittoBroker broker, string password, CertificateStore certificates)
    {
        Directory.CreateDirectory(folder);
        WriteIfChanged(Path.Combine(folder, Dockerfile), DockerfileText(image, broker));
        WriteIfChanged(Path.Combine(folder, ConfigFile), ConfigText(broker));

        var passwordPath = Path.Combine(folder, PasswordFile);
        if (broker.HasLogin)
        {
            var current = File.Exists(passwordPath) ? File.ReadAllText(passwordPath) : "";
            if (!MosquittoPassword.Matches(current, broker.Username, password))
                WritePrivate(passwordPath, MosquittoPassword.Line(broker.Username, password) + "\n");
        }
        else
        {
            File.Delete(passwordPath);
        }

        var certs = Path.Combine(folder, CertificatesFolder);
        if (broker.UsesTls && certificates.Find(broker.ServerCertificateId) is { } server)
        {
            CreatePrivateFolder(certs);
            WriteIfChanged(Path.Combine(certs, CertificateFile), File.ReadAllText(server.ChainPath));
            WritePrivate(Path.Combine(certs, KeyFile), File.ReadAllText(server.KeyPath), onlyIfChanged: true);
            var authority = Path.Combine(certs, AuthorityFile);
            if (broker.MutualTlsPort is not null)
                WriteIfChanged(authority, File.ReadAllText((certificates.Find(broker.ClientAuthorityId) ?? server).AuthorityPath));
            else
                File.Delete(authority);
        }
        else
        {
            DeleteCertificates(certs);
        }
    }

    // Only a folder whose Dockerfile Joktan wrote is a broker's, so nothing else is touched.
    private static void DeleteBroker(string folder)
    {
        if (!IsOurs(Path.Combine(folder, Dockerfile)))
            return;
        DeleteCertificates(Path.Combine(folder, CertificatesFolder));
        File.Delete(Path.Combine(folder, PasswordFile));
        DeleteIfOurs(Path.Combine(folder, ConfigFile));
        File.Delete(Path.Combine(folder, Dockerfile));
        DeleteIfEmpty(folder);
    }

    private static void DeleteCertificates(string folder)
    {
        if (!Directory.Exists(folder))
            return;
        foreach (var file in new[] { CertificateFile, KeyFile, AuthorityFile })
            File.Delete(Path.Combine(folder, file));
        DeleteIfEmpty(folder);
    }

    private static bool IsOurs(string path)
    {
        if (!File.Exists(path))
            return false;
        using var reader = new StreamReader(path);
        return reader.ReadLine()?.StartsWith(Marker, StringComparison.Ordinal) == true;
    }

    private static void DeleteIfOurs(string path)
    {
        if (IsOurs(path))
            File.Delete(path);
    }

    private static void DeleteIfEmpty(string folder)
    {
        if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
            Directory.Delete(folder);
    }

    private static void WriteIfChanged(string path, string content)
    {
        if (!File.Exists(path) || File.ReadAllText(path) != content)
            File.WriteAllText(path, content);
    }

    // The password file holds the password's hash and the certificates folder the key, so only the user may read them.
    private static void CreatePrivateFolder(string path)
    {
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(path);
        else
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static void WritePrivate(string path, string content, bool onlyIfChanged = false)
    {
        if (onlyIfChanged && File.Exists(path) && File.ReadAllText(path) == content)
            return;
        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(path, content);
            return;
        }

        using (var stream = new FileStream(path, new FileStreamOptions
               {
                   Mode = FileMode.Create,
                   Access = FileAccess.Write,
                   UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
               }))
        {
            stream.Write(Encoding.UTF8.GetBytes(content));
        }

        // A file that was already there keeps its mode, so it is set again.
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
