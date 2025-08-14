using System;
using EPiServer.Data;
using EPiServer.Data.Dynamic;
// ReSharper disable PropertyCanBeMadeInitOnly.Global
// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global

namespace SqlBlobProvider.src.EPiCode.SqlBlobProvider;

[EPiServerDataStore(AutomaticallyCreateStore = true, AutomaticallyRemapStore = true)]
public class SqlBlobModel
{
    [EPiServerDataIndex] public Uri BlobId { get; set; }
    public Identity Id { get; set; } = Guid.NewGuid();
    public byte[] Blob { get; set; }
}