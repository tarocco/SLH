using OpenMetaverse;
using OpenMetaverse.Assets;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using Unosquare.Labs.EmbedIO;
using Unosquare.Labs.EmbedIO.Constants;
using Unosquare.Labs.EmbedIO.Modules;
using Unosquare.Net;

namespace LibSLH
{
    public class TextureWebAPIController : WebApiController
    {
        protected SLHClient Client;

        public TextureWebAPIController(IHttpContext context, SLHClient client) : base(context)
        {
            Client = client;
        }

        [WebApiHandler(HttpVerbs.Get, "/texture/{uuid_str}")]
        public bool GetTextureByUUID(string uuid_str)
        {
            if (uuid_str.ToLower().EndsWith(".png"))
                uuid_str = uuid_str.Remove(uuid_str.Length - 4);
            if (UUID.TryParse(uuid_str, out UUID uuid))
            {
                var image = Client.GetTextureByUUID(uuid);
                if (image != null)
                {
                    HttpContext.Response.ContentType = "image/png";
                    HttpContext.Response.AddHeader("Content-Disposition", $"inline; filename=\"{uuid}.png\"");
                    image.Save(HttpContext.Response.OutputStream, ImageFormat.Png);
                    return true;
                }
            }
            return false;
        }
    }
}