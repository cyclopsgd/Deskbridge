using System.IO;
using Deskbridge.Core.Models;

namespace Deskbridge.Core.Interfaces;

public interface IConnectionImporter
{
    string SourceName { get; }
    string FileFilter { get; }
    Task<ImportResult> ParseAsync(Stream stream, CancellationToken ct = default);
}
