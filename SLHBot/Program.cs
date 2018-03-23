using CommandLine.Utility;
using LitJson;
using OpenMetaverse;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using static SLHBot.Utility;
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

            var config_file_path = arguments["config-file"];
            var config_arg_text = arguments["config"];
            string config_file_text = null;

            if (!IsNullOrEmpty(config_file_path))
                config_file_text = File.ReadAllText(config_file_path);

            JsonData config = new JsonData();
            JsonData websocket_config, websocket_cert_config;
            if (!IsNullOrEmpty(config_file_text))
                config = JsonMapper.ToObject(config_file_text);

            if (!IsNullOrEmpty(config_arg_text))
            {
                var arg_config = JsonMapper.ToObject(config_arg_text);
                foreach (var key in arg_config.Keys)
                    config[key] = arg_config[key];
            }

            config.TryGetValue("WebSocket", out websocket_config);
            websocket_config.TryGetValue("Certificate", out websocket_cert_config);

            TryArg(config, arguments, "FirstName", "first", out string first_name);
            TryArg(config, arguments, "LastName", "last", out string last_name);
            TryArg(config, arguments, "Password", "pass", out string password);
            TryArg(config, arguments, "LoginURI", "login-uri", out string login_uri);
            TryArg(config, arguments, "StartLocation", "start-location", out string start_location);
            TryArg(websocket_config, arguments, "Location", "ws", out string ws_location);
            TryArg(websocket_cert_config, arguments, "Path", "ws-cert-path", out string ws_cert_path);
            TryArg(websocket_cert_config, arguments, "Password", "ws-cert-pass", out string ws_cert_password);
            TryArg(config, arguments, "StandaloneAddress", "standalone-address", out string standalone_binding_address);
            TryArg(config, arguments, "DummySession", "dummy-session", out bool dummy_session);

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

            SLHClient client = null;
            var logged_out = false;

            if (dummy_session)
                Logger.Log("Using dummy session mode without Second Life client.", Helpers.LogLevel.Warning);
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

                client.Network.BeginLogin(login_params);

                while (login_status != LoginStatus.Success)
                {
                    if (login_status == LoginStatus.Failed)
                        throw new LoginFailedException(login_fail_reason);
                    Thread.Sleep(200);
                }
            }

            #endregion SLHClient

            #region WebSockets

            var server = new SLHWebSocketServer(ws_location)
            {
                Certificate = certificate
            };

            server.Start(); ;

            #endregion WebSockets

            #region SLHClient <==> WebSockets

            if (client != null)
            {
                client.Self.ChatFromSimulator += (sender, e) =>
                {
                    switch (e.Type)
                    {
                        default:
                            var json_data = new JsonData
                            {
                                ["_event"] = "ChatFromSimulator",
                                ["AudibleLevel"] = (int)e.AudibleLevel,
                                ["FromName"] = e.FromName,
                                ["Message"] = e.Message,
                                ["OwnerID"] = e.OwnerID.ToString(),
                                ["Position"] = e.Position.ToString(),
                                ["Simulator"] = new JsonData
                                {
                                    ["Name"] = e.Simulator.Name,
                                    ["Handle"] = e.Simulator.Handle
                                },
                                ["SourceType"] = (int)e.SourceType,
                                ["Type"] = (int)e.Type
                            };
                            server.BroadcastMessage(json_data);
                            break;

                        case ChatType.StartTyping:
                        case ChatType.StopTyping:
                            break;
                    }
                };

                client.Avatars.ViewerEffect += (sender, e) =>
                {
                    var json_data = new JsonData
                    {
                        ["_event"] = "ViewerEffect",
                        ["Duration"] = e.Duration,
                        ["EffectId"] = e.EffectID.ToString(),
                        ["SourceId"] = e.SourceID.ToString(),
                        ["TargetID"] = e.TargetID.ToString(),
                        ["TargetPosition"] = e.TargetPosition.ToString(),
                        ["Type"] = (int)e.Type
                    };
                    server.BroadcastMessage(json_data);
                };

                client.GetObjectNearestPoint += (sender, e) =>
                {
                    var json_data = new JsonData()
                    {
                        ["_event"] = "GetObjectNearestPoint",
                        ["Simulator"] = new JsonData()
                        {
                            ["Handle"] = e.Simulator.Handle,
                            ["Name"] = e.Simulator.Name
                        },
                        ["Object"] = new JsonData()
                        {
                            ["LocalID"] = e.Prim.LocalID
                        }
                    };
                };

                client.DebugObject += (sender, e) =>
                {
                    var primitive = client.Objects.GetPrimitive(e.Simulator, e.LocalID, UUID.Zero, false);
                    if (primitive != null)
                    {
                        var face_textures = primitive.Textures.FaceTextures;
                        var diffuse = face_textures
                            .Select(f => f.TextureID)
                            .Select(t => t.ToString());

                        var json_data = new JsonData
                        {
                            ["_event"] = "DebugObject",
                            ["Textures"] = new JsonData
                            {
                                ["Diffuse"] = JsonMapper.ToJson(diffuse)
                            }
                        };

                        server.BroadcastMessage(json_data);
                    }
                };
            }

            #endregion SLHClient <==> WebSockets

            #region Main Loop

            server.ReceivedJSONMessage += (sender, e) =>
            {
                try
                {
                    if (dummy_session)
                        Logger.Log(e.Message.ToJson(), Helpers.LogLevel.Info);
                    else
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
                    client.Network?.Logout();
                Thread.Sleep(100);
            }

            #endregion Main Loop
        }
    }
}