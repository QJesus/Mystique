﻿using Mystique.Core.DomainModel;
using System.Collections.Generic;
using System.IO;

namespace Mystique.Core.Interfaces
{
    public interface IReferenceContainer
    {
        List<CachedReferenceItemKey> GetAll();

        bool Exist(string name, string version);

        void SaveStream(string name, string version, Stream stream);

        Stream GetStream(string name, string version);
    }
}
