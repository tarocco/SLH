using CommandLine.Utility;
using LibSLH;
using LitJson;
using OpenMetaverse;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using Unosquare.Labs.EmbedIO;
using Unosquare.Labs.EmbedIO.Constants;
using Unosquare.Labs.EmbedIO.Modules;
using static LibSLH.Utility;
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

        static Program()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var file_version_info = FileVersionInfo.GetVersionInfo(assembly.Location);
            Product = file_version_info.ProductName;
            Version = file_version_info.ProductVersion;
        }

        private static bool TryArg(JsonData config, Arguments args, string config_name, string arg_name, out string value)
        {
            if (config != null && config.TryGetValue(config_name, out value))
                return true;
            if (args != null)
            {
                value = args[arg_name];
                return value != null;
            }
            value = default(string);
            return false;
        }

        private static bool TryArg(JsonData config, Arguments args, string config_name, string arg_name, out bool value)
        {
            if (config != null && config.TryGetValue(config_name, out value))
                return true;
            if (args != null && bool.TryParse(args[arg_name], out value))
                return true;
            value = default(bool);
            return false;
        }

        private static void Main(string[] args)
        {
            var shut_down_source = new CancellationTokenSource();
            //var shutting_down = false;
            Console.CancelKeyPress += (object sender, ConsoleCancelEventArgs e) =>
            {
                e.Cancel = true;
                shut_down_source.Cancel();
            };

            #region Setup

            var arguments = new Arguments(args);

            var config_file_path = arguments["config-file"] ?? "config.json";
            var config_arg_text = arguments["config"];
            string config_file_text = null;

            if (!IsNullOrEmpty(config_file_path))
            {
                if (!File.Exists(config_file_path))
                    throw new FileNotFoundException("Config file does not exist at path", config_file_path);
                config_file_text = File.ReadAllText(config_file_path);
            }

            JsonData config = new JsonData();

            if (!IsNullOrEmpty(config_file_text))
                config = JsonMapper.ToObject(config_file_text);

            if (!IsNullOrEmpty(config_arg_text))
            {
                var arg_config = JsonMapper.ToObject<Dictionary<string, JsonData>>(config_arg_text);
                foreach (var key in arg_config.Keys)
                    config[key] = arg_config[key];
            }

            config.TryGetValue("WebSocket", out JsonData websocket_config);
            websocket_config.TryGetValue("Certificate", out JsonData websocket_cert_config);
            X509Certificate2 certificate = null;

            if (!TryArg(config, arguments, "FirstName", "first", out string first_name))
                throw new ArgumentException("First name must be specified");
            TryArg(config, arguments, "LastName", "last", out string last_name);
            TryArg(config, arguments, "Password", "pass", out string password);
            if (!TryArg(config, arguments, "LoginURI", "login-uri", out string login_uri))
                login_uri = Settings.AGNI_LOGIN_SERVER;
            TryArg(config, arguments, "StartLocation", "start-location", out string start_location);
            bool ws_location_defined = TryArg(websocket_config, arguments, "Location", "ws", out string ws_location);
            bool ws_cert_path_defined = TryArg(websocket_cert_config, arguments, "Path", "ws-cert-path", out string ws_cert_path);
            if (!ws_location_defined)
            {
                if (ws_cert_path_defined)
                    ws_location = "wss://127.0.0.1:5756";
                else
                    ws_location = "ws://127.0.0.1:5756";
            }
            if (ws_cert_path_defined)
            {
                if (TryArg(websocket_cert_config, arguments, "Password", "ws-cert-pass", out string ws_cert_password))
                    certificate = new X509Certificate2(ws_cert_path, ws_cert_password);
                else
                    certificate = new X509Certificate2(ws_cert_path);
            }

            TryArg(config, arguments, "StandaloneAddress", "standalone-address", out string standalone_binding_address);
            TryArg(config, arguments, "DummySession", "dummy-session", out bool dummy_session);

            #endregion Setup

            #region Server

            var server = new SLHWebSocketServer(ws_location)
            {
                Certificate = certificate
            };

            server.Start();

            #endregion Server

            #region Client

            SLHClient client = null;
            var logged_out = false;

            #endregion Client

            if (dummy_session)
            {
                #region Standalone

                if (!IsNullOrEmpty(standalone_binding_address))
                {
                    var standalone = WebServer
                        .Create(standalone_binding_address)
                        .WithStaticFolderAt("html");
                    standalone.RunAsync(shut_down_source.Token);
                }

                #endregion Standalone

                Logger.Log("Using dummy session mode without Second Life client.", Helpers.LogLevel.Warning);
                while (!shut_down_source.IsCancellationRequested)
                    Thread.Sleep(100);
            }
            else
            {
                client = new SLHClient
                {
                    Throttle =
                    {
                        Wind = 0,
                        Cloud = 0,
                        Land = 1000000,
                        Task = 1000000,
                    }
                };

                #region Standalone

                if (!IsNullOrEmpty(standalone_binding_address))
                {
                    var standalone = WebServer
                        .Create(standalone_binding_address, RoutingStrategy.Regex)
                        .WithStaticFolderAt("html");
                    // TODO: move APIs to a separate, non-standalone web server
                    var web_api_module = new WebApiModule();
                    web_api_module.RegisterController(() => new TextureWebAPIController(client));
                    standalone.RegisterModule(web_api_module);
                    standalone.RunAsync(shut_down_source.Token);
                }

                #endregion Standalone

                var login_status = LoginStatus.None;
                string login_fail_reason = null;
                client.Network.LoginProgress += (sender, e) =>
                {
                    login_status = e.Status;
                    login_fail_reason = e.FailReason;
                };

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

                using (var slh = new SLH(client, server))
                {
                    client.Network.BeginLogin(login_params);

                    while (login_status != LoginStatus.Success)
                    {
                        if (login_status == LoginStatus.Failed)
                            throw new LoginFailedException(login_fail_reason);
                        Thread.Sleep(200);
                    }

                    while (!logged_out)
                    {
                        if (shut_down_source.IsCancellationRequested)
                            client.Network?.Logout();
                        Thread.Sleep(100);
                    }
                }
            }
        }
    }
}