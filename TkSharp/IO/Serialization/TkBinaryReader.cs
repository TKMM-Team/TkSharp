using System.Collections.ObjectModel;
using CommunityToolkit.HighPerformance;
using TkSharp.Core;
using TkSharp.Core.Extensions;
using TkSharp.Core.IO.Serialization;
using TkSharp.Core.Models;
using static TkSharp.IO.Serialization.TkBinaryWriter;

namespace TkSharp.IO.Serialization;

public static class TkBinaryReader
{
    public static TkModManager Read(in Stream input, string dataFolderPath)
    {
        TkModManager manager = new(dataFolderPath);

        if (input.Read<uint>() != MAGIC) {
            throw new InvalidDataException("Invalid mod manager magic.");
        }

        if (input.Read<uint>() != VERSION) {
            throw new InvalidDataException("Invalid mod manager version, expected 1.1.0.");
        }

        int modCount = input.Read<int>();
        for (int i = 0; i < modCount; i++) {
            manager.Mods.Add(
                ReadTkMod(input, manager)
            );
        }
        
        int profileCount = input.Read<int>();
        for (int i = 0; i < profileCount; i++) {
            manager.Profiles.Add(
                ReadTkProfile(input, manager.Mods)
            );
        }
        
        int selectedModIndex = input.Read<int>();
        if (selectedModIndex > -1) {
            manager.Selected = manager.Mods[selectedModIndex];
        }
        
        int currentProfileIndex = input.Read<int>();
        if (currentProfileIndex > -1) {
            manager.CurrentProfile = manager.Profiles[currentProfileIndex];
        }

        return manager;
    }

    internal static TkMod ReadTkMod(in Stream input, ITkSystemProvider systemProvider)
    {
        var id = input.Read<Ulid>();
        string relativeModFolderPath = id.ToString();
        ITkSystemSource source = systemProvider.GetSystemSource(relativeModFolderPath);
        
        var result = new TkMod {
            Id = id,
            Name = input.ReadString()!,
            Description = input.ReadString()!,
            Thumbnail = ReadTkThumbnail(input),
            Changelog = TkChangelogReader.Read(input, source),
            Version = input.ReadString()!,
            Author = input.ReadString()!
        };

        int contributorCount = input.Read<int>();
        for (int i = 0; i < contributorCount; i++) {
            result.Contributors.Add(
                new TkModContributor(
                    input.ReadString()!,
                    input.ReadString()!
                )
            );
        }

        int optionGroupCount = input.Read<int>();
        for (int i = 0; i < optionGroupCount; i++) {
            result.OptionGroups.Add(
                ReadTkModOptionGroup(input, systemProvider, relativeModFolderPath)
            );
        }

        int dependencyCount = input.Read<int>();
        for (int i = 0; i < dependencyCount; i++) {
            result.Dependencies.Add(
                ReadTkModDependency(input)
            );
        }
        
        return result;
    }

    private static TkModOptionGroup ReadTkModOptionGroup(in Stream input,
        ITkSystemProvider systemProvider, string parentModFolderPath)
    {
        var id = input.Read<Ulid>();
        parentModFolderPath = Path.Combine(parentModFolderPath, "options", id.ToString());
        
        var result = new TkModOptionGroup {
            Name = input.ReadString()!,
            Description = input.ReadString()!,
            Thumbnail = ReadTkThumbnail(input),
            Type = input.Read<OptionGroupType>(),
            IconName = input.ReadString(),
        };
        
        int optionCount = input.Read<int>();
        for (int i = 0; i < optionCount; i++) {
            result.Options.Add(
                ReadTkModOption(input, systemProvider, parentModFolderPath)
            );
        }
        
        int defaultSelectedOptionCount = input.Read<int>();
        for (int i = 0; i < defaultSelectedOptionCount; i++) {
            int index = input.Read<int>();
            result.DefaultSelectedOptions.Add(
                result.Options[index]
            );
        }
        
        int dependencyCount = input.Read<int>();
        for (int i = 0; i < dependencyCount; i++) {
            result.Dependencies.Add(
                ReadTkModDependency(input)
            );
        }
        
        return result;
    }

    private static TkModOption ReadTkModOption(in Stream input, ITkSystemProvider systemProvider, string parentModFolderPath)
    {
        var id = input.Read<Ulid>();
        string changelogFolderPath = Path.Combine(parentModFolderPath, id.ToString());
        ITkSystemSource source = systemProvider.GetSystemSource(changelogFolderPath);
        
        return new TkModOption {
            Id = id,
            Name = input.ReadString()!,
            Description = input.ReadString()!,
            Thumbnail = ReadTkThumbnail(input),
            Changelog = TkChangelogReader.Read(input, source)
        };
    }

    private static TkModDependency ReadTkModDependency(in Stream input)
    {
        return new TkModDependency {
            DependentName = input.ReadString()!,
            DependentId = input.Read<Ulid>(),
        };
    }

    private static TkProfile ReadTkProfile(in Stream input, ObservableCollection<TkMod> mods)
    {
        var result = new TkProfile {
            Name = input.ReadString()!,
            Description = input.ReadString()!,
            Thumbnail = ReadTkThumbnail(input),
        };
        
        int modCount = input.Read<int>();
        for (int i = 0; i < modCount; i++) {
            int index = input.Read<int>();
            result.Mods.Add(
                new TkProfileMod(mods[index]) {
                    IsEnabled = input.Read<bool>(),
                    IsEditingOptions = input.Read<bool>()
                }
            );
        }
        
        int selectedIndex = input.Read<int>();
        if (selectedIndex > -1) {
            result.Selected = result.Mods[selectedIndex];
        }
        
        return result;
    }

    private static TkThumbnail? ReadTkThumbnail(in Stream input)
    {
        return input.Read<bool>()
            ? new TkThumbnail {
                ThumbnailPath = input.ReadString()!
            }
            : null;
    }
}