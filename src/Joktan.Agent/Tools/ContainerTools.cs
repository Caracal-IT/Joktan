using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Joktan.Agent.Mcp;
using Joktan.Containers;
using Joktan.Containers.ViewModels;
using static Joktan.Agent.Tools.AgentTools;

namespace Joktan.Agent.Tools;

public static class ContainerTools
{
    private static readonly ToolParameter Project = P.Text("project", "The project's name or compose file, from containers_list.");

    private static readonly string[] Actions = ["up", "stop", "restart", "down", "down_with_volumes", "recreate", "rebuild", "pull", "build", "pause", "unpause", "kill", "remove"];

    public static IEnumerable<McpTool> Create(ContainersViewModel page) =>
    [
        ReadAsync("containers_list", "List the compose files on the Containers page and the state of their services.", [], async _ =>
        {
            await Task.WhenAll(page.Projects.Select(p => p.RefreshStatusAsync()));
            return new
            {
                Engine = page.SelectedEngine.ToString(), page.EngineText, page.Summary, page.Error,
                Projects = page.Projects.Select(p => new
                {
                    p.Name, p.File, p.Summary, p.Error,
                    Services = p.Services.Select(s => new { s.Service, Container = s.Name, s.Status, s.Ports, s.Image }).ToList(),
                }).ToList(),
            };
        }),
        Change("containers_add_file", "Add a compose file to the Containers page. It starts nothing.", [P.Text("path", "The file's path.")], args =>
        {
            page.NewComposeFile = args.Text("path");
            page.AddTypedCommand.Execute(null);
            Check(page.Error);
            return "Added.";
        }),
        Change("containers_remove_file", "Stop showing a compose file. Its containers are left as they are.", [Project], args =>
        {
            page.RemoveProject(Find(page, args));
            return "Removed.";
        }),
        Change("containers_engine", "Choose the container engine.", [P.Choice("engine", "Which.", Enum.GetNames<ContainerEngine>())], args =>
        {
            page.SelectedEngine = Enum.Parse<ContainerEngine>(args.Text("engine"), ignoreCase: true);
            return "Chosen.";
        }),
        RiskyAsync("containers_run", "Run a compose action on a project or one service. down_with_volumes also deletes what its volumes hold.",
            [Project, P.Choice("action", "What to do.", Actions), P.OptText("service", "One service; all of them otherwise.")], async args =>
            {
                var project = Find(page, args);
                var service = args.OptText("service");
                if (page.IsBusy)
                    throw new ToolFailure("Another compose command is still running.");

                page.Output.Clear();
                await (args.Text("action") switch
                {
                    "up" => project.UpCommand.ExecuteAsync(service),
                    "stop" => project.StopCommand.ExecuteAsync(service),
                    "restart" => project.RestartCommand.ExecuteAsync(service),
                    "down" => project.DownCommand.ExecuteAsync(null),
                    "down_with_volumes" => project.ConfirmDownWithVolumesCommand.ExecuteAsync(null),
                    "recreate" => project.RecreateCommand.ExecuteAsync(service),
                    "rebuild" => project.RebuildCommand.ExecuteAsync(service),
                    "pull" => project.PullCommand.ExecuteAsync(service),
                    "build" => project.BuildCommand.ExecuteAsync(service),
                    "pause" => project.PauseCommand.ExecuteAsync(service),
                    "unpause" => project.UnpauseCommand.ExecuteAsync(service),
                    "kill" => project.KillCommand.ExecuteAsync(service),
                    "remove" => project.RemoveContainerCommand.ExecuteAsync(service),
                    _ => throw new ToolFailure($"The action must be one of {string.Join(", ", Actions)}."),
                });

                await project.RefreshStatusAsync();

                // The page takes in its output on a timer.
                await Task.Delay(TimeSpan.FromMilliseconds(500));
                return new { project.Summary, Problem = project.Error ?? page.Error, Output = page.Output.TakeLast(100).ToList() };
            }),
        Risky("containers_open_shell", "Open a terminal window on the user's screen, with a shell inside a running service's container. Nothing is typed into it.", [Project, P.Text("service", "The service.")], args =>
        {
            Find(page, args).OpenShellCommand.Execute(args.Text("service"));
            return "Opened.";
        }),
        ReadAsync("containers_logs", "Read the latest logs of a project or one service.", [Project, P.OptText("service", "One service; all of them otherwise."), P.OptNumber("lines", "How many lines; 200 unless given.")], async args =>
        {
            var project = Find(page, args);
            var lines = args.Whole("lines") ?? 200;
            page.TailLines = lines;
            page.ShowLogs(project, args.OptText("service"));

            // They arrive as they are read, so give them a moment.
            await Task.Delay(TimeSpan.FromSeconds(2));
            return page.LogLines.TakeLast(lines).ToList();
        }),
        Read("containers_brokers", "List the Mosquitto brokers made on the Containers page, and the compose file they are written to. Start them with containers_run on that file's project.", [], _ => new
        {
            page.Brokers.ComposeFile, page.Brokers.Error,
            Brokers = page.Brokers.Brokers.Select(b => new
            {
                b.Name, b.Port, b.TlsPort, b.MutualTlsPort, b.Username,
                ServerCertificate = page.Brokers.Certificates.Find(b.ServerCertificateId)?.DisplayName,
                ClientCertificatesSignedBy = page.Brokers.Certificates.Find(b.ClientAuthorityId)?.DisplayName,
            }).ToList(),
        }),
        ChangeAsync("containers_broker_save",
            "Add a Mosquitto broker on the Containers page, or change the one with this name, and write it into the brokers' compose file. Leave a setting out to keep it, or for a new broker its default; an empty port turns that port off. A broker that is running is started again with the change.",
            [
                P.Text("name", "The broker's name: lower-case letters, digits, dashes and underscores."),
                P.OptText("new_name", "A new name for an existing broker."),
                P.OptText("port", "The host port for plain MQTT; empty for none. A new broker gets the first free one from 1883."),
                P.OptText("tls_port", "The host port for TLS; empty for none. Needs server_certificate."),
                P.OptText("mutual_tls_port", "The host port for mutual TLS, where clients show a certificate; empty for none. Needs server_certificate."),
                P.OptText("username", "The user clients must sign in as; empty to let anyone connect."),
                P.OptText("password", "The user's password. Kept in the credential store."),
                P.OptText("server_certificate", "The name of a server certificate on the Certificates page; empty for none."),
                P.OptText("client_ca", "The name of the CA on the Certificates page that clients' certificates must be signed by; empty for the CAs that signed the server certificate."),
            ], async args =>
            {
                var name = args.Text("name").Trim();
                var existing = page.Brokers.Brokers.FirstOrDefault(b => b.Name == name);
                var editor = new BrokerEditorViewModel(existing, page.Brokers);
                editor.NameText = args.OptText("new_name") ?? name;
                editor.PortText = args.OptText("port") ?? editor.PortText;
                editor.TlsPortText = args.OptText("tls_port") ?? editor.TlsPortText;
                editor.MutualTlsPortText = args.OptText("mutual_tls_port") ?? editor.MutualTlsPortText;
                editor.Username = args.OptText("username") ?? editor.Username;
                editor.Password = args.OptText("password") ?? "";
                if (args.OptText("server_certificate") is { } server)
                {
                    if (server.Trim().Length == 0)
                        editor.SelectedServerCertificate = editor.ServerCertificates[0];
                    else if (!editor.ChooseServerCertificate(server))
                        throw new ToolFailure($"No server certificate is called {server}. certificates_list gives them.");
                }

                if (args.OptText("client_ca") is { } authority)
                {
                    if (authority.Trim().Length == 0)
                        editor.SelectedClientAuthority = editor.ClientAuthorities[0];
                    else if (!editor.ChooseClientAuthority(authority))
                        throw new ToolFailure($"No CA is called {authority}. certificates_list gives them.");
                }

                if (page.IsBusy)
                    throw new ToolFailure("Another compose command is still running.");
                if (!await editor.SaveBrokerAsync())
                    throw new ToolFailure(editor.Error ?? "The broker could not be saved.");
                Check(page.Brokers.Error);
                return existing is null ? "Added." : "Saved.";
            }),
        RiskyAsync("containers_broker_remove", "Remove a Mosquitto broker from the Containers page. Its container is stopped and removed, and its password forgotten.", [P.Text("name", "The broker's name, from containers_brokers.")], async args =>
        {
            var name = args.Text("name").Trim();
            var row = page.Brokers.Items.FirstOrDefault(r => r.Name == name) ?? throw new ToolFailure($"No broker is called {name}. containers_brokers gives them.");
            if (page.IsBusy)
                throw new ToolFailure("Another compose command is still running.");
            await row.ConfirmRemoveCommand.ExecuteAsync(null);
            Check(page.Brokers.Error);
            return "Removed.";
        }),
    ];

    private static ComposeProjectViewModel Find(ContainersViewModel page, ToolArgs args)
    {
        var name = args.Text("project").Trim();
        var found = page.Projects.Where(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || p.File == name).ToList();
        return found.Count switch
        {
            0 => throw new ToolFailure($"No project is called {name}. containers_list gives them."),
            1 => found[0],
            _ => throw new ToolFailure($"{found.Count} projects are called {name}. Give its file instead."),
        };
    }
}
