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

            Thread acceptThread = new Thread(AcceptClients);
            acceptThread.Start();

            while (running)
            {
                string command = Console.ReadLine();
                if (command?.ToLower() == "exit")
                {
                    running = false;
                    listener.Stop();
                    Environment.Exit(0);
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
