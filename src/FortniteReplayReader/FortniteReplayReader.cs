using FortniteReplayReader.Exceptions;
using FortniteReplayReader.Extensions;
using FortniteReplayReader.Models;
using FortniteReplayReader.Models.Enums;
using FortniteReplayReader.Models.Events;
using FortniteReplayReader.Models.NetFieldExports;
using FortniteReplayReader.Models.NetFieldExports.Weapons;
using FortniteReplayReader.Models.NetFieldExports.Items;
using FortniteReplayReader.Models.World;
using System.Collections.Generic;
using System.Linq;
using FortniteReplayReader;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Unreal.Core;
using Unreal.Core.Contracts;
using Unreal.Core.Exceptions;
using Unreal.Core.Models;
using Unreal.Core.Models.Enums;
using Unreal.Encryption;
using Unreal.Core.Attributes;
using System.Text.Json; // ← Para serializar JSON

namespace FortniteReplayReader;

public class ReplayReader : Unreal.Core.ReplayReader<FortniteReplay>
{
    private FortniteReplayBuilder Builder;
    public List<WorldActor> AIPawns { get; set; } = new();
    
    // ← AGREGAR ESTO: Registro de actors
    private ActorRegistry _actorRegistry = new ActorRegistry();
    private string _currentReplayFile;

    public ReplayReader(ILogger logger = null, ParseMode parseMode = ParseMode.Minimal) : base(logger, parseMode)
    {
    }

    public FortniteReplay ReadReplay(string fileName)
    {
        _currentReplayFile = fileName;
        using var stream = File.Open(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return ReadReplay(stream);
    }

    public FortniteReplay ReadReplay(Stream stream)
    {
        using var archive = new Unreal.Core.BinaryReader(stream);

        Builder = new FortniteReplayBuilder();
        ReadReplay(archive);

        return Builder.Build(Replay);
    }

    private string _branch;
    public int Major { get; set; }
    public int Minor { get; set; }
    public string Branch
    {
        get => _branch;
        set
        {
            var regex = new Regex(@"\+\+Fortnite\+Release\-(?<major>\d+)\.(?<minor>\d*)");
            var result = regex.Match(value);
            if (result.Success)
            {
                Major = int.Parse(result.Groups["major"]?.Value ?? "0");
                Minor = int.Parse(result.Groups["minor"]?.Value ?? "0");
            }
            _branch = value;
        }
    }

    protected override void OnChannelOpened(uint channelIndex, NetworkGUID? actor)
    {
        if (actor != null)
        {
            Builder.AddActorChannel(channelIndex, actor.Value);
        }
    }

    protected override void OnChannelClosed(uint channelIndex, NetworkGUID? actor)
    {
        if (actor != null)
        {
            Builder.RemoveChannel(channelIndex);
        }
    }

    protected override void OnNetDeltaRead(uint channelIndex, NetDeltaUpdate update)
    {
        switch (update.Export)
        {
            case ActiveGameplayModifier modifier:
                Builder.UpdateGameplayModifiers(modifier);
                break;
            case SpawnMachineRepData spawnMachine:
                Builder.UpdateRebootVan(channelIndex, spawnMachine);
                break;
        }
    }

    protected override void OnExportRead(uint channelIndex, INetFieldExportGroup? exportGroup)
    {
        // Primero el switch original
        switch (exportGroup)
        {
            case GameState state:
                Builder.UpdateGameState(state);
                break;
            case PlaylistInfo playlist:
                Builder.UpdatePlaylistInfo(playlist);
                break;
            case FortPlayerState state:
                Builder.UpdatePlayerState(channelIndex, state);
                break;
            case PlayerPawn pawn:
                Builder.UpdateAIPawn(channelIndex, pawn);
                Builder.UpdatePlayerPawn(channelIndex, pawn);
                break;
            case SafeZoneIndicator safeZone:
                Builder.UpdateSafeZones(safeZone);
                break;
            case SupplyDropLlama llama:
                Builder.UpdateLlama(channelIndex, llama);
                break;
            case Models.NetFieldExports.SupplyDrop drop:
                Builder.UpdateSupplyDrop(channelIndex, drop);
                break;
            case FortPoiManager poimanager:
                Builder.UpdatePoiManager(poimanager);
                break;
            case BaseWeapon weapon:
                Builder.UpdateWeapon(channelIndex, weapon);
                break;
            case Umbramolt_Kipper umbramolt_Kipper:
                Builder.UpdateChest(channelIndex, umbramolt_Kipper);
                break;
            case CreativeChest creativeChest:
                Builder.UpdateChest(channelIndex, creativeChest);
                break;
            case FactionChest factionChest:
                Builder.UpdateChest(channelIndex, factionChest);
                break;
            case Chest chest:
                Builder.UpdateChest(channelIndex, chest);
                break;
        }
        
        // ← REGISTRAR TODOS LOS ACTORS (no solo chests)
        if (exportGroup != null)
        {
            RegisterActor(channelIndex, exportGroup);
            
            // Detectar y procesar chests específicamente
            var typeName = exportGroup.GetType().FullName ?? "";
            var pathAttribute = exportGroup.GetType()
                .GetCustomAttributes(typeof(NetFieldExportGroupAttribute), false)
                .Cast<NetFieldExportGroupAttribute>()
                .FirstOrDefault();
            
            var pathName = pathAttribute?.Path ?? "";
            
            if (typeName.Contains("Chest", StringComparison.OrdinalIgnoreCase) || 
                typeName.Contains("Container", StringComparison.OrdinalIgnoreCase) ||
                pathName.Contains("Chest", StringComparison.OrdinalIgnoreCase) ||
                pathName.Contains("Container", StringComparison.OrdinalIgnoreCase))
            {
                if (exportGroup is BaseContainer container)
                {
                    Builder.UpdateChest(channelIndex, container);
                }
            }
        }
    }

    // ← NUEVO MÉTODO: Registrar actor
    private void RegisterActor(uint channelIndex, INetFieldExportGroup exportGroup)
    {
        var typeName = exportGroup.GetType().FullName ?? "Unknown";
        var pathAttribute = exportGroup.GetType()
            .GetCustomAttributes(typeof(NetFieldExportGroupAttribute), false)
            .Cast<NetFieldExportGroupAttribute>()
            .FirstOrDefault();
        
        var pathName = pathAttribute?.Path ?? "N/A";
        
        // Evitar duplicados exactos
        var exists = _actorRegistry.Actors.Any(a => 
            a.TypeName == typeName && 
            a.PathName == pathName && 
            a.ChannelIndex == channelIndex);
        
        if (!exists)
        {
            _actorRegistry.Actors.Add(new ActorInfo
            {
                TypeName = typeName,
                PathName = pathName,
                ChannelIndex = channelIndex
            });
        }
    }

    // ← NUEVO MÉTODO: Guardar el registro al finalizar
    public void SaveActorRegistry(string outputPath = null)
    {
        if (outputPath == null)
        {
            // Si no se especifica ruta, guardar al lado del replay
            var replayDir = Path.GetDirectoryName(_currentReplayFile) ?? Directory.GetCurrentDirectory();
            var replayName = Path.GetFileNameWithoutExtension(_currentReplayFile);
            outputPath = Path.Combine(replayDir, $"{replayName}_actors.json");
        }

        _actorRegistry.TotalActorsFound = _actorRegistry.Actors.Count;
        _actorRegistry.ReplayFile = _currentReplayFile ?? "Unknown";
        _actorRegistry.ScanDate = DateTime.Now;

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var json = JsonSerializer.Serialize(_actorRegistry, options);
        File.WriteAllText(outputPath, json);

        Console.WriteLine($"✓ Actor registry saved to: {outputPath}");
        Console.WriteLine($"✓ Total actors found: {_actorRegistry.TotalActorsFound}");
    }

    // ← NUEVO MÉTODO: Obtener estadísticas
    public void PrintActorStatistics()
    {
        var grouped = _actorRegistry.Actors
            .GroupBy(a => a.TypeName)
            .OrderByDescending(g => g.Count())
            .Take(20);

        Console.WriteLine("\n=== TOP 20 MOST COMMON ACTORS ===");
        foreach (var group in grouped)
        {
            Console.WriteLine($"{group.Count(),4}x {group.Key}");
        }
        Console.WriteLine("==================================\n");
    }

    protected override void OnExternalDataRead(uint channelIndex, IExternalData? externalData)
    {
        if (externalData != null)
        {
            Builder.UpdatePrivateName(channelIndex, new PlayerNameData(externalData.Archive));
        }
    }

    public override void ReadReplayHeader(FArchive archive)
    {
        base.ReadReplayHeader(archive);
        Branch = Replay.Header.Branch;
    }

    public override void ReadEvent(FArchive archive)
    {
        var info = new EventInfo
        {
            Id = archive.ReadFString(),
            Group = archive.ReadFString(),
            Metadata = archive.ReadFString(),
            StartTime = archive.ReadUInt32(),
            EndTime = archive.ReadUInt32(),
            SizeInBytes = archive.ReadInt32()
        };

        _logger?.LogDebug("Encountered event {group} ({metadata}) at {startTime} of size {sizeInBytes}", info.Group, info.Metadata, info.StartTime, info.SizeInBytes);

        using var decryptedArchive = DecryptBuffer(archive, info.SizeInBytes);

        if (info.Group == ReplayEventTypes.PLAYER_ELIMINATION)
        {
            var elimination = ParseElimination(decryptedArchive, info);
            Replay.Eliminations.Add(elimination);
            return;
        }

        else if (info.Metadata == ReplayEventTypes.MATCH_STATS)
        {
            Replay.Stats = ParseMatchStats(decryptedArchive, info);
            return;
        }

        else if (info.Metadata == ReplayEventTypes.TEAM_STATS)
        {
            Replay.TeamStats = ParseTeamStats(decryptedArchive, info);
            return;
        }

        else if (info.Metadata == ReplayEventTypes.ENCRYPTION_KEY)
        {
            ParseEncryptionKeyEvent(decryptedArchive, info);
            return;
        }

        _logger?.LogDebug("Unknown event {group} ({metadata}) of size {sizeInBytes}", info.Group, info.Metadata, info.SizeInBytes);
        if (IsDebugMode)
        {
            throw new UnknownEventException($"Unknown event {info.Group} ({info.Metadata}) of size {info.SizeInBytes}");
        }
    }

    public virtual EncryptionKey ParseEncryptionKeyEvent(FArchive archive, EventInfo info) => new()
    {
        Info = info,
        Key = archive.ReadBytesToString(32)
    };

    public virtual TeamStats ParseTeamStats(FArchive archive, EventInfo info) => new()
    {
        Info = info,
        Unknown = archive.ReadUInt32(),
        Position = archive.ReadUInt32(),
        TotalPlayers = archive.ReadUInt32()
    };

    public virtual Stats ParseMatchStats(FArchive archive, EventInfo info) => new()
    {
        Info = info,
        Unknown = archive.ReadUInt32(),
        Accuracy = archive.ReadSingle(),
        Assists = archive.ReadUInt32(),
        Eliminations = archive.ReadUInt32(),
        WeaponDamage = archive.ReadUInt32(),
        OtherDamage = archive.ReadUInt32(),
        Revives = archive.ReadUInt32(),
        DamageTaken = archive.ReadUInt32(),
        DamageToStructures = archive.ReadUInt32(),
        MaterialsGathered = archive.ReadUInt32(),
        MaterialsUsed = archive.ReadUInt32(),
        TotalTraveled = archive.ReadUInt32()
    };

    public virtual PlayerElimination ParseElimination(FArchive archive, EventInfo info)
    {
        try
        {
            var elim = new PlayerElimination
            {
                Info = info,
            };

            var version = archive.ReadInt32();

            if (version >= 3)
            {
                archive.SkipBytes(1);

                if (version >= 6)
                {
                    elim.EliminatedInfo.Rotation = archive.ReadFQuat();
                    elim.EliminatedInfo.Location = archive.ReadFVector();
                    elim.EliminatedInfo.Scale = archive.ReadFVector();
                }

                elim.EliminatorInfo.Rotation = archive.ReadFQuat();
                elim.EliminatorInfo.Location = archive.ReadFVector();
                elim.EliminatorInfo.Scale = archive.ReadFVector();
            }
            else
            {
                if (Major <= 4 && Minor < 2)
                {
                    archive.SkipBytes(8);
                }
                else if (Major == 4 && Minor <= 2)
                {
                    archive.SkipBytes(36);
                }
            }

            if ((int) archive.EngineNetworkVersion >= 34)
            {
                archive.SkipBytes(80);
            }

            ParsePlayer(archive, elim.EliminatedInfo, version);
            ParsePlayer(archive, elim.EliminatorInfo, version);

            elim.GunType = archive.ReadByte();
            elim.Knocked = archive.ReadUInt32AsBoolean();
            elim.Time = info.StartTime.MillisecondsToTimeStamp();
            return elim;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error while parsing PlayerElimination at timestamp {}", info?.StartTime);
            throw new PlayerEliminationException($"Error while parsing PlayerElimination at timestamp {info?.StartTime}", ex);
        }
    }

    public virtual void ParsePlayer(FArchive archive, PlayerEliminationInfo info, int version)
    {
        if (version < 6)
        {
            info.Id = archive.ReadFString();
            return;
        }

        info.PlayerType = archive.ReadByteAsEnum<PlayerTypes>();
        info.Id = info.PlayerType switch
        {
            PlayerTypes.BOT => "Bot",
            PlayerTypes.NAMED_BOT => archive.ReadFString(),
            PlayerTypes.PLAYER => archive.ReadGUID(archive.ReadByte()),
            _ => ""
        };
    }

    protected override FArchive DecryptBuffer(FArchive archive, int size)
    {
        if (!Replay.Info.IsEncrypted)
        {
            return new Unreal.Core.BinaryReader(archive.ReadBytes(size))
            {
                EngineNetworkVersion = Replay.Header.EngineNetworkVersion,
                NetworkVersion = Replay.Header.NetworkVersion,
                ReplayHeaderFlags = Replay.Header.Flags,
                ReplayVersion = Replay.Info.FileVersion
            };
        }

        var key = Replay.Info.EncryptionKey;
        var encryptedBytes = archive.ReadBytes(size);

        using var aesCryptoServiceProvider = new AesCryptoServiceProvider
        {
            KeySize = key.Length * 8,
            Key = key.ToArray(),
            Mode = CipherMode.ECB,
            Padding = PaddingMode.PKCS7
        };

        using var cryptoTransform = aesCryptoServiceProvider.CreateDecryptor();
        var decryptedArray = cryptoTransform.TransformFinalBlock(encryptedBytes.ToArray(), 0, encryptedBytes.Length);

        return new Unreal.Core.BinaryReader(decryptedArray.AsMemory())
        {
            EngineNetworkVersion = archive.EngineNetworkVersion,
            NetworkVersion = archive.NetworkVersion,
            ReplayHeaderFlags = archive.ReplayHeaderFlags,
            ReplayVersion = archive.ReplayVersion
        };
    }

    protected override FArchive Decompress(FArchive archive)
    {
        if (!Replay.Info.IsCompressed)
        {
            return archive;
        }

        var decompressedSize = archive.ReadInt32();
        var compressedSize = archive.ReadInt32();
        var compressedBuffer = archive.ReadBytes(compressedSize);

        _logger?.LogDebug("Decompressed archive from {compressedSize} to {decompressedSize}.", compressedSize, decompressedSize);
        var output = Oodle.DecompressReplayData(compressedBuffer, decompressedSize);

        return new Unreal.Core.BinaryReader(output)
        {
            EngineNetworkVersion = archive.EngineNetworkVersion,
            NetworkVersion = archive.NetworkVersion,
            ReplayHeaderFlags = archive.ReplayHeaderFlags,
            ReplayVersion = archive.ReplayVersion
        };
    }
}