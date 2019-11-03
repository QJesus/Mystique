using Mystique.Core.Exceptions;
using Newtonsoft.Json;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

namespace Mystique.Core.DomainModel
{
    public class PluginPackage
    {
        private string tempFolderName;
        private string folderName;
        private Stream zipStream;

        public PluginModel Configuration { get; private set; }

        public async Task InitializeAsync(Stream zipStream)
        {
            var archive = new ZipArchive(this.zipStream = zipStream, ZipArchiveMode.Read);
            zipStream.Position = 0;
            tempFolderName = Path.Combine(Environment.CurrentDirectory, "Mystique_plugins", Guid.NewGuid().ToString());
            archive.ExtractToDirectory(tempFolderName, true);

            var folder = new DirectoryInfo(tempFolderName);
            var files = folder.GetFiles();
            var configFile = files.SingleOrDefault(p => p.Name == "plugin.json");

            if (configFile == null)
            {
                throw new MissingConfigurationFileException();
            }

            using var stream = configFile.OpenRead();
            using var sr = new StreamReader(stream);
            var content = await sr.ReadToEndAsync();
            Configuration = JsonConvert.DeserializeObject<PluginModel>(content);

            if (Configuration == null)
            {
                throw new WrongFormatConfigurationException();
            }
        }

        public void SetupFolder()
        {
            var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
            zipStream.Position = 0;
            folderName = Path.Combine(Environment.CurrentDirectory, "Mystique_plugins", Configuration.Name);
            archive.ExtractToDirectory(folderName, true);

            var folder = new DirectoryInfo(tempFolderName);
            if (folder.Exists)
            {
                folder.Delete(true);
            }
        }
    }
}
