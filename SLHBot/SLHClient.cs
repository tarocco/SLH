using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LitJson;
using OpenMetaverse;

namespace SLHBot
{
    public class SLHClient : GridClient
    {
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
            }
        }

        public void OnSay(string message)
        {
            Self.Chat(message, 0, ChatType.Normal);
        }

        public void OnTeleportToAvatar(string avatar_name)
        {
            var avatars = GetAllAvatars();
            var avatar = avatars.First(a => a.Name == avatar_name);
            var avatar_forward = Vector3.UnitX * avatar.Rotation;
            Self.Teleport(avatar.RegionHandle, avatar.Position, avatar_forward);
        }
    }
}
