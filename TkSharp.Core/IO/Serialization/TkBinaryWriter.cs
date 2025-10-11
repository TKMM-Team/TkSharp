using CommunityToolkit.HighPerformance;
using TkSharp.Core.Extensions;
using TkSharp.Core.IO.Serialization.Models;
using TkSharp.Core.Models;

namespace TkSharp.Core.IO.Serialization;

public static class TkBinaryWriter
{
    public const uint TKPK_MAGIC = 0x504D4B54;
    public const uint TKPK_VERSION = 0x20;
    
    public static void WriteTkMod(in Stream output, in TkMod mod, TkLookupContext? context = null)
    {
        context ??= new TkLookupContext();
        
        WriteTkStoredItem(output, mod);
        output.WriteString(mod.Version);
        output.WriteString(mod.Author);

        output.Write(mod.Contributors.Count);
        foreach (var contributor in mod.Contributors) {
            output.WriteString(contributor.Author);
            output.WriteString(contributor.Contribution);
        }

        output.Write(mod.OptionGroups.Count);
        
        for (int i = 0; i < mod.OptionGroups.Count; i++) {
            var optionGroup = mod.OptionGroups[i];
            WriteTkModOptionGroup(output, optionGroup, context);
            context.Groups[optionGroup] = i;
        }

        output.Write(mod.Dependencies.Count);
        foreach (var dependency in mod.Dependencies) {
            WriteTkModDependency(output, dependency);
        }
    }

    public static void WriteTkModOptionGroup(in Stream output, in TkModOptionGroup optionGroup, TkLookupContext context)
    {
        WriteTkItem(output, optionGroup);
        output.Write(optionGroup.Type);
        output.WriteString(optionGroup.IconName);
        output.Write(optionGroup.Priority);
        
        output.Write(optionGroup.Options.Count);
        for (int i = 0; i < optionGroup.Options.Count; i++) {
            var option = optionGroup.Options[i];
            WriteTkModOption(output, option);
            context.Options[option] = i;
        }

        output.Write(optionGroup.DefaultSelectedOptions.Count);
        foreach (var selectedOptionIndex in optionGroup.DefaultSelectedOptions) {
            output.Write(context.Options[selectedOptionIndex]);
        }

        output.Write(optionGroup.Dependencies.Count);
        foreach (var dependency in optionGroup.Dependencies) {
            WriteTkModDependency(output, dependency);
        }
    }
    
    public static void WriteTkModOption(in Stream output, in TkModOption item)
    {
        WriteTkStoredItem(output, item);
        output.Write(item.Priority);
    }

    public static void WriteTkModDependency(in Stream output, in TkModDependency dependency)
    {
        output.WriteString(dependency.DependentName);
        output.Write(dependency.DependentId);
    }

    public static void WriteTkProfile(in Stream output, TkProfile profile, TkLookupContext lookup)
    {
        output.Write(profile.Id);
        WriteTkItem(output, profile);
        
        output.Write(profile.Mods.Count);
        foreach (var mod in profile.Mods) {
            WriteTkProfileMod(output, mod, lookup);
        }
        
        output.Write(
            profile.Selected is not null ? profile.Mods.IndexOf(profile.Selected) : -1
        );
    }

    public static void WriteTkProfileMod(in Stream output, TkProfileMod mod, TkLookupContext lookup)
    {
        output.Write(lookup.Mods[mod.Mod]);
        output.Write(mod.IsEnabled);
        output.Write(mod.IsEditingOptions);
        
        output.Write(mod.SelectedOptions.Count(x => x.Value.Count != 0));
        
        foreach (var (groupKey, selection) in mod.SelectedOptions) {
            if (selection.Count == 0) {
                continue;
            }
            
            output.Write(lookup.Groups[groupKey]);
            output.Write(selection.Count);

            foreach (var option in selection) {
                output.Write(lookup.Options[option]);
            }
        }
    }

    public static void WriteTkStoredItem(in Stream output, in TkStoredItem item)
    {
        output.Write(item.Id);
        WriteTkItem(output, item);
        TkChangelogWriter.Write(output, item.Changelog);
    }

    public static void WriteTkItem(in Stream output, in TkItem item)
    {
        output.WriteString(item.Name);
        output.WriteString(item.Description);
        WriteTkThumbnail(output, item.Thumbnail);
    }

    public static void WriteTkThumbnail(in Stream output, in TkThumbnail? thumbnail)
    {
        bool writeThumbnail = !(thumbnail is null || thumbnail.IsDefault);
        
        output.Write(writeThumbnail);
        if (writeThumbnail) {
            output.WriteString(thumbnail!.RelativeThumbnailPath ?? thumbnail!.ThumbnailPath);
        }
    }
}