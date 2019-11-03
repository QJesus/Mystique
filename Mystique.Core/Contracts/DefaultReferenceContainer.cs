using Mystique.Core.DomainModel;
using Mystique.Core.Interfaces;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Mystique.Core.Contracts
{
    public class DefaultReferenceContainer : IReferenceContainer
    {
        private static readonly Dictionary<CachedReferenceItemKey, Stream> cachedReferences = new Dictionary<CachedReferenceItemKey, Stream>();

        public List<CachedReferenceItemKey> GetAll() => cachedReferences.Keys.ToList();

        public bool Exist(string name, string version) => cachedReferences.Keys.Any(p => p.ReferenceName == name && p.Version == version);

        public void SaveStream(string name, string version, Stream stream)
        {
            if (Exist(name, version))
            {
                return;
            }
            cachedReferences.Add(new CachedReferenceItemKey { ReferenceName = name, Version = version }, stream);
        }

        public Stream GetStream(string name, string version)
        {
            var key = cachedReferences.Keys.FirstOrDefault(p => p.ReferenceName == name && p.Version == version);

            if (key != null)
            {
                cachedReferences[key].Position = 0;
                return cachedReferences[key];
            }

            return null;
        }
    }
}
