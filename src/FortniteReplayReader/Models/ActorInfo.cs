using System.Collections.Generic;

namespace FortniteReplayReader.Models;

public class ActorInfo
{
    public string TypeName { get; set; }
    public string PathName { get; set; }
    public uint ChannelIndex { get; set; }
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