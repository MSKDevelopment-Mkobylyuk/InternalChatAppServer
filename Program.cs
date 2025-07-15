using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace InternalChatAppServer
{
    class Program
    {
        static List<TcpClient> clients = new List<TcpClient>();
        static Dictionary<string, List<TcpClient>> groups =
            new Dictionary<string, List<TcpClient>>();
        static TcpListener listener;
        static bool running = true;

        static void Main(string[] args)
        {
            Console.Title = "Chat Server";
            Console.WriteLine("Starting server...");

            listener = new TcpListener(IPAddress.Any, 5000);
            listener.Start();

            Console.WriteLine("Server started on port 5000.");
            Console.WriteLine("Waiting for clients...");
            Console.WriteLine("Commands: addgroup <GroupName>, listgroups, exit");

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
                    string groupName = arg;
                    if (!groups.ContainsKey(groupName))
                    {
                        groups[groupName] = new List<TcpClient>();
                        Console.WriteLine($"Group '{groupName}' created.");
                        // No broadcast here
                    }
                    else
                    {
                        Console.WriteLine($"Group '{groupName}' already exists.");
                    }
                }
                else if (cmd == "listgroups")
                {
                    Console.WriteLine("Groups:");
                    foreach (var g in groups.Keys)
                        Console.WriteLine(" - " + g);
                }
                else
                {
                    Console.WriteLine("Unknown command.");
                }
            }
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
                }
                catch { }
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
                        catch { }
                    }
                }
            }
        }
    }
}
