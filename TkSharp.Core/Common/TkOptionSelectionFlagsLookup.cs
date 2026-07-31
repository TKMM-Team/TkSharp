using TkSharp.Core.Models;

namespace TkSharp.Core.Common;

public sealed class TkOptionSelectionFlagsLookup(TkModOption target, TkModOptionGroup group)
{
    private bool GetIsDefaultSelected()
        => group.DefaultSelectedOptions.Contains(target);

    public void SetIsDefaultSelected(bool value)
    {
        if (value) {
            AddDefaultSelected(target);
            return;
        }

        if (!CanChangeDefaultSelected()) {
            target.SetSelectionFlagsFromCollections(isDefaultSelected: true);
            return;
        }

        RemoveDefaultSelected(target);
    }

    public bool CanChangeDefaultSelected()
    {
        if (!GetIsDefaultSelected()) {
            return true;
        }

        return !(
            group.DefaultSelectedOptions.Count == 1
            && group.Type is OptionGroupType.MultiRequired or OptionGroupType.SingleRequired
        );
    }

    private void AddDefaultSelected(TkModOption option)
    {
        switch (group.Type) {
            case OptionGroupType.Single or OptionGroupType.SingleRequired:
                foreach (var existing in group.DefaultSelectedOptions.ToArray()) {
                    if (existing == option) {
                        continue;
                    }

                    RemoveDefaultSelected(existing);
                }

                break;
        }

        if (!group.DefaultSelectedOptions.Contains(option)) {
            group.DefaultSelectedOptions.Add(option);
        }

        option.SetSelectionFlagsFromCollections(isDefaultSelected: true);
        UpdateSelectionFlags();
    }

    private void RemoveDefaultSelected(TkModOption option)
    {
        group.DefaultSelectedOptions.Remove(option);
        option.SetSelectionFlagsFromCollections(isDefaultSelected: false);
        UpdateSelectionFlags();
    }

    private void UpdateSelectionFlags()
    {
        foreach (var option in group.Options) {
            option.UpdateSelectionFlags();
        }
    }

    public static void EnsureValidDefaultSelections(TkModOptionGroup group)
    {
        if (group.Type is OptionGroupType.Single or OptionGroupType.SingleRequired
            && group.DefaultSelectedOptions.Count > 1) {
            var keep = group.DefaultSelectedOptions[0];
            group.DefaultSelectedOptions.Clear();
            group.DefaultSelectedOptions.Add(keep);
        }

        if (group.Type is OptionGroupType.MultiRequired or OptionGroupType.SingleRequired
            && group.DefaultSelectedOptions.Count == 0
            && group.Options.FirstOrDefault() is { } first) {
            group.DefaultSelectedOptions.Add(first);
        }

        foreach (var option in group.Options) {
            option.SetSelectionFlagsFromCollections(group.DefaultSelectedOptions.Contains(option));
            option.UpdateSelectionFlags();
        }
    }
}
