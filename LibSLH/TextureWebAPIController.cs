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

        public TextureWebAPIController(SLHClient client) : base()
        {
            Client = client;
        }

        [WebApiHandler(HttpVerbs.Get, "/texture/{uuid_str}")]
        public bool GetTextureByUUID(WebServer server, HttpListenerContext context, string uuid_str)
        {
            if (uuid_str.ToLower().EndsWith(".png"))
                uuid_str = uuid_str.Remove(uuid_str.Length - 4);
            if (UUID.TryParse(uuid_str, out UUID uuid))
            {
                var download = Client.Assets.Cache.GetCachedImage(uuid);
                //ManagedImage managed_image;
                Image image = null;
                if (download != null)
                {
                    //OpenJPEG.DecodeToImage(download.AssetData, out managed_image, out image);
                    image = CSJ2K.J2kImage.FromBytes(download.AssetData);
                }
                else
                {
                    var reset = new AutoResetEvent(false);
                    TextureDownloadCallback callback = (TextureRequestState state, AssetTexture asset) =>
                    {
                        if (state == TextureRequestState.Finished)
                        {
                            if (state == TextureRequestState.Finished)
                            {
                                //OpenJPEG.DecodeToImage(asset.AssetData, out managed_image, out image);
                                image = CSJ2K.J2kImage.FromBytes(asset.AssetData);
                                reset.Set();
                            }
                        }
                    };
                    Client.Assets.RequestImage(uuid, ImageType.Normal, callback);
                    reset.WaitOne(Client.Settings.TRANSFER_TIMEOUT);
                }
                if (image != null)
                {
                    context.Response.ContentType = "image/png";
                    context.Response.AddHeader("Content-Disposition", $"inline; filename=\"{uuid}.png\"");
                    image.Save(context.Response.OutputStream, ImageFormat.Png);
                    return true;
                }
            }
            return false;
        }
    }
}