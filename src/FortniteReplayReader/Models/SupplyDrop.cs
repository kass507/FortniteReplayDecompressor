using Unreal.Core.Models;
 
namespace FortniteReplayReader.Models;
 
public class SupplyDrop
{
    public SupplyDrop()
    {
 
    }
 
    public SupplyDrop(uint channelIndex, NetFieldExports.SupplyDrop drop)
    {
        Id = channelIndex;
        FallHeight = drop.FallHeight;
        FallSpeed = drop.FallSpeed;
        FallDirection = drop.FallDirection;
    }
 
    public uint Id { get; set; }
    public bool HasSpawnedPickups { get; set; }
    public bool Looted { get; set; }
    public float? LootedTime { get; set; }
    public double? LootedTimeDouble { get; set; }
    public bool BalloonPopped { get; set; }
    public float? BalloonPoppedTime { get; set; }
    public double? BalloonPoppedTimeDouble { get; set; }
    public double FallSpeed { get; set; }
    public FVector LandingLocation { get; set; }
    public double FallHeight { get; set; }
    public FVector FallDirection { get; set; }
}
