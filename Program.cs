using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace InternalChatAppServer
{
    class Program
    {
        static List<TcpClient> clients = new List<TcpClient>();
        static Dictionary<string, List<TcpClient>> groups = new Dictionary<string, List<TcpClient>>();

        static TcpListener listener;
        static bool running = true;
        static int serverPort = 5000; // default port
        static readonly string settingsFile = "server_settings.json";

        static void Main(string[] args)
        {
            Console.Title = "Chat Server";
            Console.WriteLine("Starting server...");

            // Load settings (groups + port) from JSON file
            LoadSettings();

            StartListener();

            Console.WriteLine($"Server started on port {serverPort}.");
            Console.WriteLine("Waiting for clients...");
            Console.WriteLine("Commands: addgroup <GroupName>, listgroups, setport <PortNumber>, exit");

            Thread acceptThread = new Thread(AcceptClients);
            acceptThread.Start();

            while (running)
            {
                string command = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(command))
                    continue;

                string cmd;
                string arg = null;

                int spaceIndex = command.IndexOf(' ');
                if (spaceIndex == -1)
                {
                    cmd = command.ToLower();
                }
                else
                {
                    cmd = command.Substring(0, spaceIndex).ToLower();
                    arg = command.Substring(spaceIndex + 1).Trim();
                }

                if (cmd == "exit")
                {
                    running = false;
                    listener.Stop();
                    Environment.Exit(0);
                }
                else if (cmd == "addgroup" && !string.IsNullOrEmpty(arg))
                {
                    AddGroup(arg);
                }
                else if (cmd == "listgroups")
                {
                    ListGroups();
                }
                else if (cmd == "setport" && !string.IsNullOrEmpty(arg))
                {
                    SetPort(arg);
                }
                else
                {
                    Console.WriteLine("Unknown command.");
                }
            }
        }

        static void AddGroup(string groupName)
        {
            if (!groups.ContainsKey(groupName))
            {
                groups[groupName] = new List<TcpClient>();
                Console.WriteLine($"Group '{groupName}' created.");
                BroadcastGroupList();
                SaveSettings();
            }
            else
            {
                Console.WriteLine($"Group '{groupName}' already exists.");
            }
        }

        static void ListGroups()
        {
            Console.WriteLine("Groups:");
            foreach (var g in groups.Keys)
                Console.WriteLine(" - " + g);
        }

        static void SetPort(string arg)
        {
            if (int.TryParse(arg, out int newPort) && newPort >= 1 && newPort <= 65535)
            {
                if (newPort == serverPort)
                {
                    Console.WriteLine($"Port is already set to {newPort}.");
                    return;
                }

                Console.WriteLine($"Changing port from {serverPort} to {newPort}...");

                try
                {
                    listener.Stop();
                }
                catch { /* ignore errors on stopping */ }

                serverPort = newPort;
                StartListener();
                SaveSettings();

                Console.WriteLine($"Port changed successfully to {serverPort}.");
            }
            else
            {
                Console.WriteLine("Invalid port number. Must be between 1 and 65535.");
            }
        }

        static void StartListener()
        {
            listener = new TcpListener(IPAddress.Any, serverPort);
            listener.Start();
        }

        static void AcceptClients()
        {
            while (running)
            {
                try
                {
                    TcpClient client = listener.AcceptTcpClient();
                    lock (clients)
                        clients.Add(client);
                    Console.WriteLine("Client connected.");

                    Thread clientThread = new Thread(() => HandleClient(client));
                    clientThread.Start();

                    SendGroupListToClient(client);
                }
                catch
                {
                    // Ignore listener errors on shutdown or restart
                }
            }
        }

        static void HandleClient(TcpClient client)
        {
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[1024];
            int bytesRead;

            try
            {
                while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) != 0)
                {
                    string msg = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    Console.WriteLine(msg); // Display on server
                    BroadcastMessage(msg, client);
                }
            }
            catch
            {
                Console.WriteLine("Client disconnected.");
            }

            lock (clients)
                clients.Remove(client);
            client.Close();
        }

        static void BroadcastMessage(string message, TcpClient excludeClient)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(message);

            lock (clients)
            {
                foreach (var client in clients)
                {
                    if (client != excludeClient)
                    {
                        try
                        {
                            client.GetStream().Write(buffer, 0, buffer.Length);
                        }
                        catch
                        {
                            // Ignore individual write errors
                        }
                    }
                }
            }
        }

        static void BroadcastGroupList()
        {
            string groupMessage = "GROUPS:" + string.Join(",", groups.Keys);
            byte[] buffer = Encoding.UTF8.GetBytes(groupMessage);

            lock (clients)
            {
                foreach (var client in clients)
                {
                    try
                    {
                        client.GetStream().Write(buffer, 0, buffer.Length);
                    }
                    catch
                    {
                        // Ignore write errors
                    }
                }
            }
        }

        static void SendGroupListToClient(TcpClient client)
        {
            string groupMessage = "GROUPS:" + string.Join(",", groups.Keys);
            byte[] buffer = Encoding.UTF8.GetBytes(groupMessage);

            try
            {
                client.GetStream().Write(buffer, 0, buffer.Length);
            }
            catch
            {
                // Ignore write errors to single client
            }
        }

        // Load settings (groups and port) from JSON file
        static void LoadSettings()
        {
            if (!File.Exists(settingsFile))
            {
                // No file, use defaults
                serverPort = 5000;
                groups = new Dictionary<string, List<TcpClient>>();
                return;
            }

            try
            {
                string json = File.ReadAllText(settingsFile);
                var data = JsonSerializer.Deserialize<ServerSettings>(json);
                serverPort = data.Port;
                groups = new Dictionary<string, List<TcpClient>>();
                foreach (var groupName in data.Groups ?? new List<string>())
                {
                    groups[groupName] = new List<TcpClient>();
                }
                Console.WriteLine("Settings loaded.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading settings: {ex.Message}");
                // fallback defaults
                serverPort = 5000;
                groups = new Dictionary<string, List<TcpClient>>();
            }
        }

        // Save groups and port to JSON file
        static void SaveSettings()
        {
            try
            {
                var data = new ServerSettings
                {
                    Port = serverPort,
                    Groups = new List<string>(groups.Keys)
                };
                string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(settingsFile, json);
                Console.WriteLine("Settings saved.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving settings: {ex.Message}");
            }
        }

        // Helper class to serialize/deserialize settings
        private class ServerSettings
        {
            public int Port { get; set; }
            public List<string> Groups { get; set; }
        }
    }
}
