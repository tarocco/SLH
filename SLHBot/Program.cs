using CommandLine.Utility;
using Fleck;
using LitJson;
using OpenMetaverse;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using static System.String;

namespace SLHBot
{
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

            #region Arguments

            var arguments = new Arguments(args);

            // TODO
            //if (arguments["help"] != null)
            //{
            //    Usage();
            //    return;
            //}

            string
                first_name,
                last_name,
                password,
                login_uri,
                start_location,
                ws_location,
                ws_cert_path,
                ws_cert_password,
                standalone_binding_address;

            var config_file_path = arguments["config-file"];
            if (!IsNullOrEmpty(config_file_path))
            {
                var config_file_text = File.ReadAllText(config_file_path);
                var config = JsonMapper.ToObject(config_file_text);
                config.TryGetValue("FirstName", out first_name);
                config.TryGetValue("LastName", out last_name);
                config.TryGetValue("Password", out password);
                config.TryGetValue("LoginURI", out login_uri);
                config.TryGetValue("StartLocation", out start_location);
                config.TryGetValue("WebSocketLocation", out ws_location);
                config.TryGetValue("WebSocketCertificatePath", out ws_cert_path);
                config.TryGetValue("WebSocketCertificatePassword", out ws_cert_password);
                config.TryGetValue("StandaloneWebUIBindingAddress", out standalone_binding_address);
            }
            else
            {
                first_name = arguments["first"];
                last_name = arguments["last"];
                password = arguments["pass"];
                login_uri = arguments["login-uri"];
                start_location = arguments["start-location"];
                ws_location = arguments["ws"];
                ws_cert_path = arguments["ws-cert-path"];
                ws_cert_password = arguments["ws-cert-pass"];
                standalone_binding_address = arguments["standalone-web-ui-binding-address"];
            }

            #endregion Arguments



            if (IsNullOrEmpty(login_uri))
                login_uri = Settings.AGNI_LOGIN_SERVER;

            if (IsNullOrEmpty(first_name))
                throw new ArgumentException("First name must be specified");

            #region Standalone

            if (!IsNullOrEmpty(standalone_binding_address))
            {
                // KISS HTTP server
                var listener = new HttpListener();
                var prefix = $"http://{standalone_binding_address}/";
                listener.Prefixes.Add(prefix);
                Task.Run(async () =>
                {
                    listener.Start();
                    for (; ; )
                    {
                        var context = await listener.GetContextAsync();
                        string response;
                        HttpStatusCode http_status_code;
                        try
                        {
                            var request_path = context.Request.RawUrl.TrimStart('/');
                            var root = new Uri(Directory.GetCurrentDirectory() + "/html/");
                            var file_path_uri = new Uri(root, request_path);
                            if (!root.IsBaseOf(file_path_uri))
                                throw new Exception("Bad file path URI");
                            if (file_path_uri.AbsolutePath.EndsWith("/"))
                                file_path_uri = new Uri(file_path_uri, "index.html");
                            using (var reader = new StreamReader(file_path_uri.AbsolutePath))
                                response = reader.ReadToEnd();
                            http_status_code = HttpStatusCode.OK;
                        }
                        catch (Exception ex)
                        {
                            response = $"{ex.Message}\r\n{ex.StackTrace}";
                            http_status_code = HttpStatusCode.InternalServerError;
                        }

                        context.Response.StatusCode = (int)http_status_code;
                        using (var writer = new StreamWriter(context.Response.OutputStream))
                        {
                            await writer.WriteAsync(response);
                            await writer.FlushAsync();
                        }
                    }
                });

                Console.WriteLine($"Running standalone http server on {prefix}");
            }

            #endregion Standalone

            #region SLHClient

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

            // Legacy fix
            if (IsNullOrEmpty(last_name))
                last_name = "Resident";

            if (IsNullOrEmpty(password))
            {
                Console.Write($"Password for {first_name} {last_name}: ");
                password = GetPassword();
            }

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

            #endregion SLHClient

            #region WebSockets

            var all_sockets = new List<IWebSocketConnection>();
            var server = new WebSocketServer(ws_location);

            if (IsNullOrEmpty(ws_location))
            {
                if (IsNullOrEmpty(ws_cert_path))
                    ws_location = "ws://127.0.0.1:5756";
                else
                    ws_location = "wss://127.0.0.1:5756";
            }

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

            #endregion WebSockets

            #region Main Loop

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

            #endregion Main Loop
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