using Unreal.Core.Models;

namespace FortniteReplayReader.Models.World;

public class WorldActor
{
    public uint ChannelId { get; set; }

    public FVector? Location { get; set; }
    public FRotator? Rotation { get; set; }

    public float? Time { get; set; }
    public double? TimeDouble { get; set; }

    public string? ActorType { get; set; }
}
