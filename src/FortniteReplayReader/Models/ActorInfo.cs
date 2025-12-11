using System.Collections.Generic;

namespace FortniteReplayReader.Models;

public class ActorInfo
{
    public string TypeName { get; set; }           // Nombre de clase C# (si existe)
    public string FullTypeName { get; set; }       // Nombre completo (si existe)
    public string PathName { get; set; }           // Del replay (SIEMPRE existe)
    public uint PathNameIndex { get; set; }        // Índice interno
    public uint ChannelIndex { get; set; }         
    public bool HasCSharpClass { get; set; }       // ¿Tiene clase definida?
    public bool IsDeserialized { get; set; }       // ¿Se deserializó correctamente?
}

public class ActorRegistry
{
    public List<ActorInfo> Actors { get; set; } = new();
    public int TotalActorsFound { get; set; }
    public int ActorsWithClass { get; set; }
    public int ActorsWithoutClass { get; set; }
    public string ReplayFile { get; set; }
    public System.DateTime ScanDate { get; set; }
}