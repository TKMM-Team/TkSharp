using System.Collections.ObjectModel;
using CommunityToolkit.HighPerformance;
using TkSharp.Core.Extensions;
using TkSharp.Core.Models;

namespace TkSharp.Core.IO.Serialization;

public static class TkBinaryReader
{
    public static TkMod ReadTkMod(TkModContext context, in Stream input, ITkSystemProvider systemProvider,
        TkSystemVersion systemVersion = TkSystemVersion.Latest)
    {
        context.EnsureId(input.Read<Ulid>());
        
        var relativeModFolderPath = context.Id.ToString();
        var source = systemProvider.GetSystemSource(relativeModFolderPath);
        
        var result = new TkMod {
            Id = context.Id,
            Name = input.ReadString()!,
            Description = input.ReadString()!,
            Thumbnail = ReadTkThumbnail(input),
            Changelog = TkChangelogReader.Read(input, source, systemVersion),
            Version = input.ReadString()!,
            Author = input.ReadString()!
        };
        
        var contributorCount = input.Read<int>();
        for (var i = 0; i < contributorCount; i++) {
            result.Contributors.Add(
                new TkModContributor(
                    input.ReadString()!,
                    input.ReadString()!
                )
            );
        }

        var optionGroupCount = input.Read<int>();
        for (var i = 0; i < optionGroupCount; i++) {
            result.OptionGroups.Add(
                ReadTkModOptionGroup(input, systemProvider, relativeModFolderPath)
            );
        }

        var dependencyCount = input.Read<int>();
        for (var i = 0; i < dependencyCount; i++) {
            result.Dependencies.Add(
                ReadTkModDependency(input)
            );
        }
        
        return result;
    }

    public static TkModOptionGroup ReadTkModOptionGroup(in Stream input,
        ITkSystemProvider systemProvider, string parentModFolderPath)
    {
        var result = new TkModOptionGroup {
            Name = input.ReadString()!,
            Description = input.ReadString()!,
            Thumbnail = ReadTkThumbnail(input),
            Type = input.Read<OptionGroupType>(),
            IconName = input.ReadString(),
            Priority = input.Read<int>()
        };
        
        var optionCount = input.Read<int>();
        for (var i = 0; i < optionCount; i++) {
            result.Options.Add(
                ReadTkModOption(input, systemProvider, parentModFolderPath)
            );
        }
        
        var defaultSelectedOptionCount = input.Read<int>();
        for (var i = 0; i < defaultSelectedOptionCount; i++) {
            var index = input.Read<int>();
            result.DefaultSelectedOptions.Add(
                result.Options[index]
            );
        }
        
        var dependencyCount = input.Read<int>();
        for (var i = 0; i < dependencyCount; i++) {
            result.Dependencies.Add(
                ReadTkModDependency(input)
            );
        }
        
        return result;
    }

    public static TkModOption ReadTkModOption(in Stream input, ITkSystemProvider systemProvider, string parentModFolderPath)
    {
        var id = input.Read<Ulid>();
        var changelogFolderPath = Path.Combine(parentModFolderPath, id.ToString());
        var source = systemProvider.GetSystemSource(changelogFolderPath);
        
        return new TkModOption {
            Id = id,
            Name = input.ReadString()!,
            Description = input.ReadString()!,
            Thumbnail = ReadTkThumbnail(input),
            Changelog = TkChangelogReader.Read(input, source),
            Priority = input.Read<int>()
        };
    }

    public static TkModDependency ReadTkModDependency(in Stream input)
    {
        return new TkModDependency {
            DependentName = input.ReadString()!,
            DependentId = input.Read<Ulid>(),
        };
    }

    public static TkProfile ReadTkProfile(in Stream input, ObservableCollection<TkMod> mods)
    {
        var result = new TkProfile {
            Id = input.Read<Ulid>(),
            Name = input.ReadString()!,
            Description = input.ReadString()!,
            Thumbnail = ReadTkThumbnail(input),
        };
        
        var modCount = input.Read<int>();
        for (var i = 0; i < modCount; i++) {
            var index = input.Read<int>();
            result.Mods.Add(ReadTkProfileMod(input, mods[index]));
        }
        
        var selectedIndex = input.Read<int>();
        if (selectedIndex > -1) {
            result.Selected = result.Mods[selectedIndex];
        }
        
        return result;
    }

    public static TkProfileMod ReadTkProfileMod(in Stream input, TkMod mod)
    {
        TkProfileMod result = new(mod) {
            IsEnabled = input.Read<bool>(),
            IsEditingOptions = input.Read<bool>(),
        };
        
        var selectionGroupCount = input.Read<int>();

        for (var i = 0; i < selectionGroupCount; i++) {
            var groupKeyIndex = input.Read<int>();
            var indexCount = input.Read<int>();
            var group = mod.OptionGroups[groupKeyIndex];
            HashSet<TkModOption> selection = result.SelectedOptions[group] = [];

            for (var _ = 0; _ < indexCount; _++) {
                selection.Add(group.Options[input.Read<int>()]);
            }
        }

        result.EnsureOptionSelection();
        
        return result;
    }

    public static TkThumbnail? ReadTkThumbnail(in Stream input)
    {
        return input.Read<bool>()
            ? new TkThumbnail {
                ThumbnailPath = input.ReadString()!
            }
            : null;
    }
}