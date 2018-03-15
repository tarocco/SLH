using CommandLine.Utility;
using Fleck;
using LitJson;
using OpenMetaverse;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using static System.String;
using static SLHBot.Utility;

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

        static Program()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var file_version_info = FileVersionInfo.GetVersionInfo(assembly.Location);
            Product = file_version_info.ProductName;
            Version = file_version_info.ProductVersion;
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

            if (IsNullOrEmpty(ws_location))
            {
                if (IsNullOrEmpty(ws_cert_path))
                    ws_location = "ws://127.0.0.1:5756";
                else
                    ws_location = "wss://127.0.0.1:5756";
            }

            X509Certificate2 certificate = null;

            if (ws_location.ToLower().StartsWith("wss://"))
            {
                if (!IsNullOrEmpty(ws_cert_path))
                {
                    if (IsNullOrEmpty(ws_cert_password))
                        certificate = new X509Certificate2(ws_cert_path);
                    else
                        certificate = new X509Certificate2(ws_cert_path, ws_cert_password);
                }
            }

            #endregion Arguments

            if (IsNullOrEmpty(login_uri))
                login_uri = Settings.AGNI_LOGIN_SERVER;

            if (IsNullOrEmpty(first_name))
                throw new ArgumentException("First name must be specified");

            #region Standalone

            if (!IsNullOrEmpty(standalone_binding_address))
            {
                var standalone = new Standalone();
                standalone.Run(standalone_binding_address);
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

            var server = new SLHWebSocketServer(ws_location)
            {
                Certificate = certificate
            };

            server.Start();

            Console.WriteLine($"Running SLH WebSockets server on {ws_location}");

            #endregion WebSockets

            #region Main Loop

            server.ReceivedJSONMessage += (sender, e) =>
            {
                try
                {
                    client.ProcessMessage(e.Message);
                }
                catch (Exception ex)
                {
                    Logger.Log("Failed to process message.", Helpers.LogLevel.Error, ex);
                }
            };

            while (!logged_out)
            {
                if (shutting_down)
                    client.Network.Logout();
                Thread.Sleep(100);
            }

            #endregion Main Loop
        }
    }
}