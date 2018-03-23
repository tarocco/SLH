using LitJson;
using OpenMetaverse;
using System;
using System.Linq;

namespace LibSLH
{
    public class SLH : IDisposable
    {
        private readonly SLHClient Client;
        private readonly SLHWebSocketServer Server;

        public SLH(SLHClient client, SLHWebSocketServer server)
        {
            Client = client;
            Server = server;

            Client.Self.ChatFromSimulator += HandleChatFromSimulator;
            Client.Avatars.ViewerEffect += HandleViewerEffect;
            Client.GetObjectNearestPoint += HandleGetObjectNearestPoint;
            Client.DebugObject += HandleDebugObject;

            Server.ReceivedJSONMessage += HandleReceivedJSONMessage;
        }

        private void HandleChatFromSimulator(object sender, OpenMetaverse.ChatEventArgs args)
        {
            switch (args.Type)
            {
                default:
                    var json_data = new JsonData
                    {
                        ["_event"] = "ChatFromSimulator",
                        ["AudibleLevel"] = (int)args.AudibleLevel,
                        ["FromName"] = args.FromName,
                        ["Message"] = args.Message,
                        ["OwnerID"] = args.OwnerID.ToString(),
                        ["Position"] = args.Position.ToString(),
                        ["Simulator"] = new JsonData
                        {
                            ["Name"] = args.Simulator.Name,
                            ["Handle"] = args.Simulator.Handle
                        },
                        ["SourceType"] = (int)args.SourceType,
                        ["Type"] = (int)args.Type
                    };
                    Server.BroadcastMessage(json_data);
                    break;

                case ChatType.StartTyping:
                case ChatType.StopTyping:
                    break;
            }
        }

        private void HandleViewerEffect(object sender, ViewerEffectEventArgs args)
        {
            var json_data = new JsonData
            {
                ["_event"] = "ViewerEffect",
                ["Duration"] = args.Duration,
                ["EffectId"] = args.EffectID.ToString(),
                ["SourceId"] = args.SourceID.ToString(),
                ["TargetID"] = args.TargetID.ToString(),
                ["TargetPosition"] = args.TargetPosition.ToString(),
                ["Type"] = (int)args.Type
            };
            Server.BroadcastMessage(json_data);
        }

        private void HandleGetObjectNearestPoint(object sender, SLHClient.GetObjectNearestPointEventArgs args)
        {
            var json_data = new JsonData()
            {
                ["_event"] = "GetObjectNearestPoint",
                ["Simulator"] = new JsonData()
                {
                    ["Handle"] = args.Simulator.Handle,
                    ["Name"] = args.Simulator.Name
                },
                ["Object"] = new JsonData()
                {
                    ["LocalID"] = args.Prim.LocalID
                }
            };
        }

        private void HandleDebugObject(object sender, SLHClient.DebugObjectEventArgs args)
        {
            var primitive = Client.Objects.GetPrimitive(args.Simulator, args.LocalID, UUID.Zero, false);
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

                Server.BroadcastMessage(json_data);
            }
        }

        private void HandleReceivedJSONMessage(object sender, SLHWebSocketServer.JSONMessageEventArgs args)
        {
            try
            {
                Client.ProcessMessage(args.Message);
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to process message.", Helpers.LogLevel.Error, ex);
            }
        }

        public void Dispose()
        {
            Client.Self.ChatFromSimulator -= HandleChatFromSimulator;
            Client.Avatars.ViewerEffect -= HandleViewerEffect;
            Client.GetObjectNearestPoint -= HandleGetObjectNearestPoint;
            Client.DebugObject -= HandleDebugObject;

            Server.ReceivedJSONMessage -= HandleReceivedJSONMessage;
        }
    }
}