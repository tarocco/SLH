using KdTree;
using KdTree.Math;
using LitJson;
using OpenMetaverse;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LibSLH
{
    public class SLHClient : GridClient
    {
        private KdTree<float, Primitive> ObjectProximalLookup;
        private Dictionary<Primitive, float[]> ObjectPositions;

        public class DebugObjectEventArgs : EventArgs
        {
            //public UUID ObjectID;
            public Simulator Simulator;

            public uint LocalID;
        }

        public event EventHandler<DebugObjectEventArgs> DebugObject;

        public class GetObjectNearestPointEventArgs : EventArgs
        {
            public Simulator Simulator;
            public Vector3 Point;
            public Primitive Prim;
        }

        public event EventHandler<GetObjectNearestPointEventArgs> GetObjectNearestPoint;

        public SLHClient() : base()
        {
            ObjectProximalLookup = new KdTree<float, Primitive>(3, new FloatMath(), AddDuplicateBehavior.Update);
            ObjectPositions = new Dictionary<Primitive, float[]>();
            Objects.ObjectUpdate += HandleObjectUpdate;
            Self.IM += HandleInstantMessage;
        }

        private void HandleObjectUpdate(object sender, PrimEventArgs e)
        {
            var prim = e.Prim;
            if (ObjectPositions.TryGetValue(prim, out float[] old_point))
                ObjectProximalLookup.RemoveAt(old_point);
            var position = prim.Position;
            float[] new_point = new float[] { position.X, position.Y, position.Z };
            ObjectPositions[prim] = new_point;
            ObjectProximalLookup.Add(new_point, prim);
        }

        private void HandleInstantMessage(object sender, InstantMessageEventArgs e)
        {
            OnInstantMessage(e.Simulator, e.IM);
        }

        public async Task<List<DirectoryManager.AgentSearchData>> SearchAvatarsByName(string avatar_name)
        {
            return await Task.Run(() =>
            {
                UUID query_id = new UUID();
                List<DirectoryManager.AgentSearchData> avatars = null;
                var reset = new AutoResetEvent(false);
                EventHandler<DirPeopleReplyEventArgs> reply_handler = (sender, args) =>
                {
                    if (args.QueryID == query_id)
                    {
                        avatars = args.MatchedPeople;
                        reset.Set();
                    }
                };
                Directory.DirPeopleReply += reply_handler;
                query_id = Directory.StartPeopleSearch(avatar_name, 0);
                // TODO: use a different timeout parameter
                reset.WaitOne(Settings.RESEND_TIMEOUT);
                return avatars;
            });
        }

        public IEnumerable<Avatar> GetAllAvatars()
        {
            return Network.Simulators.SelectMany(s => s.ObjectsAvatars.Copy().Values);
        }

        public void ProcessMessage(JsonData message_body)
        {
            var action = (string)message_body["Action"];

            switch (action)
            {
                case "Say":
                    OnSay((string)message_body["Message"]);
                    break;

                case "TeleportToAvatar":
                    var avatar_name = (string)message_body["AvatarName"];
                    OnTeleportToAvatar(avatar_name);
                    break;

                case "DebugObject":
                    {
                        //var object_id = new UUID((string)message_body["ObjectID"]);
                        var local_id = (uint)message_body["LocalID"];
                        var simulator_handle = (ulong)message_body["Simulator"]["Handle"];
                        var simulator = Network.Simulators.First(s => s.Handle == simulator_handle);
                        OnDebugObject(simulator, local_id);
                    }
                    break;

                case "GetObjectNearestPoint":
                    {
                        var point = Vector3.Parse((string)message_body["Point"]);
                        var simulator_handle = (ulong)message_body["Simulator"]["Handle"];
                        //var simulator = Network.Simulators.First(s => s.Handle == simulator_handle);
                        uint globalX, globalY;
                        Utils.LongToUInts(simulator_handle, out globalX, out globalY);
                        var simulator = Network.Simulators.FirstOrDefault(s => s.Handle == simulator_handle);
                        OnGetObjectNearestPoint(simulator, point);
                    }
                    break;
            }
        }

        public void OnDebugObject(Simulator simulator, uint local_id)
        {
            DebugObject?.Invoke(this, new DebugObjectEventArgs() { LocalID = local_id });
        }

        public void OnGetObjectNearestPoint(Simulator simulator, Vector3 point)
        {
            var point_key = new[] { point.X, point.Y, point.Z };
            var node = ObjectProximalLookup.GetNearestNeighbours(point_key, 1);
            var prim = (Primitive)node.GetValue(0);
            var args = new GetObjectNearestPointEventArgs()
            {
                Point = point,
                Prim = prim,
                Simulator = simulator
            };
            GetObjectNearestPoint?.Invoke(this, args);
        }

        public void OnSay(string message)
        {
            Self.Chat(message, 0, ChatType.Normal);
        }

        public async void OnTeleportToAvatar(string avatar_name)
        {
            var search_avatars = await SearchAvatarsByName(avatar_name);
            var avatar_data = search_avatars.First(a => $"{a.FirstName} {a.LastName}" == avatar_name);
            var agent_id = avatar_data.AgentID;
            //var region_avatars = GetAllAvatars().ToArray();
            var agent_simulator = Network.Simulators
                .First(s => s.AvatarPositions.ContainsKey(agent_id));
            var agent_position = agent_simulator.AvatarPositions[agent_id];
            var avatar_forward = Vector3.UnitX;
            Self.Teleport(agent_simulator.Handle, agent_position, avatar_forward);
        }

        public void OnInstantMessage(Simulator simulator, InstantMessage instant_message)
        {
            if (instant_message.Dialog == InstantMessageDialog.RequestTeleport)
            {
                Self.TeleportLureRespond(
                    instant_message.FromAgentID,
                    instant_message.IMSessionID,
                    true);
            }
        }
    }
}