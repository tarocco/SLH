using CommandLine.Utility;
using Fleck;
using LitJson;
using OpenMetaverse;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using static System.String;

namespace SLHBot
{
    internal class CommandLineArgumentsException : Exception
    {
    }

    internal class LoginFailedException : Exception
    {
        public readonly string FailReason;

        public LoginFailedException(string fail_reason)
        {
            FailReason = fail_reason;
        }
    }

    internal class Program
    {
        public static readonly string Product;
        public static readonly string Version;
        public static readonly string[] SupportedMessageProtocols;

        static Program()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var file_version_info = FileVersionInfo.GetVersionInfo(assembly.Location);
            Product = file_version_info.ProductName;
            Version = file_version_info.ProductVersion;
            SupportedMessageProtocols = new[] { "SLH-Message-Protocol-0001" };
        }

        private static void Main(string[] args)
        {
            var shutting_down = false;
            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                shutting_down = true;
            };

            var arguments = new Arguments(args);

            // TODO
            //if (arguments["help"] != null)
            //{
            //    Usage();
            //    return;
            //}

            var login_uri = arguments["login-uri"];
            if (IsNullOrEmpty(login_uri))
                login_uri = Settings.AGNI_LOGIN_SERVER;

            var start_location = arguments["start-location"];

            Logger.Log("Using login URI " + login_uri, Helpers.LogLevel.Info);

            var first_name = arguments["first"];
            var last_name = arguments["last"];
            var password = arguments["pass"];

            var ws_location = arguments["ws"];
            var ws_cert_path = arguments["ws-cert-path"];
            var ws_cert_password = arguments["ws-cert-pass"];

            // Legacy fix
            if (IsNullOrEmpty(last_name))
                last_name = "Resident";

            if (IsNullOrEmpty(password))
            {
                Console.Write($"Password for {first_name} {last_name}: ");
                password = GetPassword();
            }

            if (IsNullOrEmpty(first_name))
                throw new ArgumentException("First name must be specified");

            if (IsNullOrEmpty(ws_location))
            {
                if (IsNullOrEmpty(ws_cert_path))
                    ws_location = "ws://127.0.0.1:5756";
                else
                    ws_location = "wss://127.0.0.1:5756";
            }

            var all_sockets = new List<IWebSocketConnection>();
            var server = new WebSocketServer(ws_location);

            if (ws_location.ToLower().StartsWith("wss://"))
            {
                if (!IsNullOrEmpty(ws_cert_path))
                {
                    if (IsNullOrEmpty(ws_cert_password))
                        server.Certificate = new X509Certificate2(ws_cert_path);
                    else
                        server.Certificate = new X509Certificate2(ws_cert_path, ws_cert_password);
                }
            }

            var client = new SLHClient
            {
                Throttle =
                {
                    Wind = 0,
                    Cloud = 0,
                    Land = 1000000,
                    Task = 1000000,
                }
            };

            var login_status = LoginStatus.None;
            string login_fail_reason = null;
            client.Network.LoginProgress += (sender, e) =>
            {
                login_status = e.Status;
                login_fail_reason = e.FailReason;
            };

            var logged_out = false;
            client.Network.LoggedOut += (sender, e) =>
            {
                logged_out = true;
            };

            var login_params = client.Network.DefaultLoginParams(first_name, last_name, password, Product, Version);

            if (!IsNullOrEmpty(start_location))
                login_params.Start = start_location;

            if (!IsNullOrEmpty(login_uri))
                login_params.URI = login_uri;

            client.Network.BeginLogin(login_params);

            while (login_status != LoginStatus.Success)
            {
                if (login_status == LoginStatus.Failed)
                    throw new LoginFailedException(login_fail_reason);
                Thread.Sleep(200);
            }

            server.SupportedSubProtocols = SupportedMessageProtocols;
            var message_queue = new LocklessQueue<JsonData>();

            server.Start(socket =>
            {
                socket.OnOpen += () =>
                {
                    all_sockets.Add(socket);
                };

                socket.OnClose += () =>
                {
                    all_sockets.Remove(socket);
                };

                socket.OnMessage += message =>
                {
                    try
                    {
                        JsonData data;
                        try
                        {
                            data = JsonMapper.ToObject(message);
                        }
                        catch (Exception ex)
                        {
                            throw new ArgumentException("Failed to parse JSON message.", ex);
                        }
                        try
                        {
                            message_queue.Enqueue(data);
                        }
                        catch (Exception ex)
                        {
                            throw new ArgumentException("Could not enqueue message.", ex);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log(ex.Message, Helpers.LogLevel.Error);
                    }
                };
            });

            while (!logged_out)
            {
                if (message_queue.TryDequeue(out JsonData message_body))
                {
                    try
                    {
                        client.ProcessMessage(message_body);
                    }
                    catch (Exception ex)
                    {
                        Logger.Log("Failed to process message.", Helpers.LogLevel.Error, ex);
                    }
                }
                if (shutting_down)
                    client.Network.Logout();
                Thread.Sleep(50);
            }
        }

        public static string GetPassword()
        {
            var password = "";
            while (true)
            {
                var i = Console.ReadKey(true);
                if (i.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    break;
                }
                if (i.Key == ConsoleKey.Backspace)
                {
                    if (password.Length > 0)
                    {
                        password.Remove(password.Length - 1);
                    }
                }
                else
                {
                    password += i.KeyChar;
                }
            }
            return password;
        }
    }
}