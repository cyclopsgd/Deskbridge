using System.IO;
using Deskbridge.Core.Interfaces;
using Deskbridge.Core.Models;

namespace Deskbridge.Core.Services;

public class MRemoteNGImporter : IConnectionImporter
{
    public string SourceName => throw new NotImplementedException();
    public string FileFilter => throw new NotImplementedException();

    public Task<ImportResult> ParseAsync(Stream stream, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }
}
