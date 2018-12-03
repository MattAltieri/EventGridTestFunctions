using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Azure.EventGrid.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace EventGridTestFunctionsMaltieri {
    public static class Thumbnail {
        
        private static readonly string BLOB_STORAGE_CONNECTION_STRING = Environment.GetEnvironmentVariable("AzureWebJobsStorage");

        private static string GetBlobNameFromUrl(string blobUrl) {
            var uri = new Uri(blobUrl);
            var cloudBlob = new CloudBlob(uri);
            return cloudBlob.Name;
        }

        private static IImageEncoder GetEncoder(string extension) {

            IImageEncoder encoder = null;

            extension = extension.Replace(".", "");
            var isSupported = Regex.IsMatch(extension, "gif|png|jpe?g", RegexOptions.IgnoreCase);

            if (isSupported) {
                switch (extension)
                {
                    case "png":
                        encoder = new PngEncoder();
                        break;
                    case "jpg":
                    case "jpeg":
                        encoder = new JpegEncoder();
                        break;
                    case "gif":
                        encoder = new GifEncoder();
                        break;
                    default:
                        break;
                }
            }

            return encoder;
        }

        [FunctionName("Thumbnail")]
        public static async Task Run([EventGridTrigger]EventGridEvent eventGridEvent, [Blob("{data.url}", FileAccess.Read)]Stream input, ILogger log) {

            try {
                if (input != null) {
                    var createdEvent = ((JObject)eventGridEvent.Data).ToObject<StorageBlobCreatedEventData>();
                    var extension = Path.GetExtension(createdEvent.Url);
                    var encoder = GetEncoder(extension);

                    if (encoder != null) {
                        var thumbnailWidth = Convert.ToInt32(Environment.GetEnvironmentVariable("THUMBNAIL_WIDTH"));
                        var thumbContainerName = Environment.GetEnvironmentVariable("THUMBNAIL_CONTAINER_NAMME");
                        var storageAccount = CloudStorageAccount.Parse(BLOB_STORAGE_CONNECTION_STRING);
                        var blobClient = storageAccount.CreateCloudBlobClient();
                        var container = blobClient.GetContainerReference(thumbContainerName);
                        var blobName = GetBlobNameFromUrl(createdEvent.Url);
                        var blockBlobb = container.GetBlockBlobReference(blobName);

                        using (var output = new MemoryStream())
                        using (Image<Rgba32> image = Image.Load(input)) {
                            
                            var divisor = image.Width / thumbnailWidth;
                            var height = Convert.ToInt32(Math.Round((decimal)(image.Height / divisor)));

                            image.Mutate(x => x.Resize(thumbnailWidth, height));
                            image.Save(output, encoder);
                            output.Position = 0;
                            await blockBlobb.UploadFromStreamAsync(output);
                        }
                    } else
                        log.LogInformation($"No encoder support for: {createdEvent.Url}.");
                }
            } catch (Exception e) {
                log.LogInformation(e.Message);
                throw;
            }
        }
    }
}